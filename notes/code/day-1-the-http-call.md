# Day 1 — The HTTP call

Concept note: [08-the-request-body.md](../08-the-request-body.md)
Code: [`Chat.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Chat.cs) (~430 lines, the whole "AI" layer)

---

## Technology used

| Thing | What it actually is | Namespace |
| --- | --- | --- |
| Transport | HTTP POST, `application/json` | `System.Net.Http` |
| JSON building | `JsonObject` / `JsonArray` (mutable DOM) | `System.Text.Json.Nodes` |
| JSON reading | `JsonDocument` / `JsonElement` (read-only, low allocation) | `System.Text.Json` |
| Auth | `Authorization: Bearer` or Azure's `api-key` header | `System.Net.Http.Headers` |
| Escaping | `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` | `System.Text.Encodings.Web` |
| Protocol | OpenAI **Chat Completions** wire format | — |

No SDK. No `Azure.AI.OpenAI`, no `OpenAI` package. The protocol is simple enough to write by hand, and writing it by hand is why you can now read any provider's docs.

### Why two JSON APIs?

| API | Mutable? | Used for | Why |
| --- | --- | --- | --- |
| `JsonObject` (Nodes) | yes | **building** the request | you add `tools` conditionally |
| `JsonDocument` | no | **reading** the response | faster, lower allocation, read-once |

Using `JsonDocument` to build would mean re-parsing strings. Using `JsonNode` to read would allocate a full object graph you only touch twice.

---

## Types, in dependency order

```
ToolCall         record   (Id, Name, ArgumentsJson)
ToolDefinition   record   (Name, Description, ParametersSchema)
ChatMessage      record   (Role, Content, ToolCallId?, ToolCalls?)
ChatTurn         record   (Content?, FinishReason, ToolCalls)

IChatClient      interface  <- the seam everything else depends on
ChatRequest      static     <- builds the JSON body
ModelEndpoint    class      <- builds the URL + auth header

OpenAiChatClient  : IChatClient   (real, HTTP)
ScriptedChatClient: IChatClient   (fake, rules)
StubChatClient    : IChatClient   (stub, fixed text)
RecordingChatClient: IChatClient  (decorator)
OpenAiEmbedder    : IEmbedder     (real, HTTP)
```

All four `IChatClient` implementations are interchangeable. That is the entire reason the project runs offline and the tests need no key.

---

## The interface

```csharp
public interface IChatClient
{
    Task<ChatTurn> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken);
}
```

Three design decisions in three parameters:

| Parameter | Decision |
| --- | --- |
| `messages` is the **whole** list | the server is stateless, so there is no "send one message" API |
| `tools` is **nullable** | `null` = the model must answer. This is the `ask` / `agent` switch. |
| `CancellationToken` | network calls must be cancellable |

`IReadOnlyList` rather than `List`: the callee must not mutate the caller's conversation.

---

## `ChatRequest.Build` — the request body

```csharp
public static JsonObject Build(
    string model,
    IReadOnlyList<ChatMessage> messages,
    IReadOnlyList<ToolDefinition>? tools)
{
    var body = new JsonObject
    {
        ["model"] = model,
        ["temperature"] = Temperature,                                  // 0.1
        ["messages"] = new JsonArray(messages.Select(MessageJson).ToArray())
    };

    if (tools is { Count: > 0 })
    {
        body["tool_choice"] = "auto";
        body["tools"] = new JsonArray(tools.Select(ToolJson).ToArray());
    }

    return body;
}
```

**Why this is a separate static class and not a private method on the client:** `trace` calls it too. One builder means the trace output cannot drift from what is actually posted. If it were private and duplicated, the two would disagree within a month.

`tools is { Count: > 0 }` is a C# property pattern — null-check and count-check in one expression.

### `tool_choice` values

| Value | Meaning |
| --- | --- |
| `"auto"` | model decides: answer or call tools ← we use this |
| `"none"` | must answer |
| `"required"` | must call a tool |
| `{"type":"function","function":{"name":"x"}}` | must call `x` |

---

## `OpenAiChatClient.CompleteAsync` — reading the response

The response shape:

```json
{
  "choices": [
    {
      "finish_reason": "stop",
      "message": { "role": "assistant", "content": "...", "tool_calls": [ ... ] }
    }
  ]
}
```

```csharp
var choice = payload.RootElement.GetProperty("choices")[0];
var message = choice.GetProperty("message");

var finish = choice.TryGetProperty("finish_reason", out var f) ? f.GetString() ?? "stop" : "stop";

var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
    ? c.GetString() : null;
```

**`GetProperty` vs `TryGetProperty` is a deliberate distinction:**

| Call | Behaviour | Used for |
| --- | --- | --- |
| `GetProperty` | throws if absent | `choices`, `message` — absent means a broken server |
| `TryGetProperty` | returns false | `content`, `tool_calls` — legitimately absent |

`content` is genuinely null when the model calls a tool instead of answering. Treating that as an error would break the agent.

### The arguments quirk, worth knowing

```csharp
var argumentsJson = arguments.ValueKind == JsonValueKind.String
    ? arguments.GetString() ?? "{}"
    : arguments.GetRawText();
```

Per the spec, `arguments` is a **string containing JSON**:

```json
"arguments": "{\"service\":\"billing-api\"}"
```

Some servers send a real object instead. This handles both. That is not defensive programming for its own sake — it is a real incompatibility you will hit the first time you switch providers.

### `finish_reason` — the field people ignore

| Value | Meaning | What you should do |
| --- | --- | --- |
| `stop` | finished normally | use the answer |
| `length` | **cut off mid-sentence** | the answer is incomplete — truncated JSON, half a word |
| `tool_calls` | wants a function | run it |
| `content_filter` | blocked by safety | surface it |

We capture it but do not act on it. In production, `length` must be handled or you will parse a truncated JSON object and get a confusing error far from the cause.

---

## `ModelEndpoint.For` — the URL factory

Same JSON body, two very different URLs:

```csharp
// Azure: model name in the PATH, api-version required, custom header
$"{endpoint}/openai/deployments/{model}/chat/completions?api-version={version}"
    header: "api-key: <key>"

// Everything else: model name in the BODY, standard header
$"{endpoint}/chat/completions"
    header: "Authorization: Bearer <key>"
```

| | Azure OpenAI | OpenAI-compatible |
| --- | --- | --- |
| Model name | in the URL path ("deployment") | in the JSON body |
| Version | `?api-version=` required | none |
| Auth header | `api-key` | `Authorization: Bearer` |

`Uri.EscapeDataString` on the deployment name, because it is user-supplied and goes into a path segment.

A test locks both shapes: `Azure_and_compatible_endpoints_build_different_urls`. This URL is one of the most common real bugs when people start.

---

## `PostAsync` — the plumbing

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, uri);
request.Content = new StringContent(body.ToJsonString(ChatRequest.Wire), Encoding.UTF8, "application/json");
```

`ChatRequest.Wire` and `ChatRequest.Readable` share one `JavaScriptEncoder`, so the trace escapes identically to the real post. Without that, `—` would appear as `—` in one and not the other.

Header handling has a branch because `Authorization` is a *typed* header in `HttpClient` and `api-key` is not:

```csharp
if (_endpoint.ApiKeyHeader == "Authorization")
    request.Headers.Authorization = AuthenticationHeaderValue.Parse(_endpoint.ApiKeyValue);
else
    request.Headers.TryAddWithoutValidation(_endpoint.ApiKeyHeader, _endpoint.ApiKeyValue);
```

`TryAddWithoutValidation` is required — `HttpClient` rejects unknown headers through the normal `Add`.

Errors include the URL and 400 characters of the server's body, because the provider puts the real reason there ("model not found", "quota exceeded") and the default .NET message tells you nothing.

---

## The four `IChatClient` implementations

| Class | Kind | Lines | Purpose |
| --- | --- | --- | --- |
| `OpenAiChatClient` | real | ~100 | HTTP to a model |
| `ScriptedChatClient` | **fake** | ~40 | working rules, drives the agent loop offline |
| `StubChatClient` | **stub** | ~10 | returns fixed text, for one test |
| `RecordingChatClient` | **decorator** | ~20 | captures requests, delegates onward |

Stub vs fake is a real distinction (Martin Fowler's terms) and it comes up in interviews:

- a **stub** returns canned answers — `StubChatClient` always returns the same string
- a **fake** has a working implementation, just not production-grade — `ScriptedChatClient` genuinely decides which tool to call

The decorator is the elegant one:

```csharp
public Task<ChatTurn> CompleteAsync(messages, tools, ct)
{
    _requests.Add(ChatRequest.Build(_model, messages, tools));   // observe
    return _inner.CompleteAsync(messages, tools, ct);            // delegate
}
```

`AgentLoop` has no idea it is being watched, so what `trace agent` prints is the real traffic.

---

## What you would use in production

| This project | Production |
| --- | --- |
| hand-written HTTP | `Microsoft.Extensions.AI` (`IChatClient`, `IEmbeddingGenerator`) — the standard .NET abstraction, almost the same shape as ours |
| no retry | `Microsoft.Extensions.Http.Resilience` / Polly — 429 and 5xx need backoff |
| `new HttpClient()` | `IHttpClientFactory` — avoids socket exhaustion and stale DNS |
| whole response at once | streaming (SSE) so the user sees tokens appear |
| no token counting | `Tiktoken` or the provider's usage field — you cannot control cost you do not measure |
| no logging | OpenTelemetry spans per call |
| key from env var | Azure Key Vault + Managed Identity |

**`Microsoft.Extensions.AI` is the one to look at next.** Its `IChatClient` is nearly identical to ours — which is a good sign that the shape here is the right one.

---

## Interview questions on this code

1. Why is `tools` nullable instead of an empty list?
2. Why `JsonObject` to build but `JsonDocument` to read?
3. `content` came back null. Is that an error?
4. What does `finish_reason: "length"` mean and why does it matter for `extract`?
5. Why does `ChatRequest` exist as a separate class instead of a private method?
6. Name the difference between `ScriptedChatClient` and `StubChatClient`.
7. How would you add retry without touching `AgentLoop`?

*(7 is the good one — the answer is another decorator, which is why the interface seam matters.)*

---

## Try it

```powershell
# the request, never sent
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"

# prove ask sends no tools array
dotnet run --project src/RunbookCopilot -- trace ask "x" | Select-String '"tools"'
```
