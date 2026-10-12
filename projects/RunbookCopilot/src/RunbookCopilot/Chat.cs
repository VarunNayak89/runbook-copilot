// ============================================================================
// Chat.cs - this is the whole "AI" part of the application.
//
// There is no model inside this file. There is no magic.
// This file only does one thing: send an HTTP request, and read the reply.
//
// If you have ever written a client for a REST API, you already know this code.
// The only new idea is WHAT we put in the request body.
//
// Read the file in this order:
//   1. The small records below  - the shapes of the data
//   2. ChatRequest              - builds the JSON we send
//   3. ModelEndpoint            - builds the URL we send it to
//   4. OpenAiChatClient         - does the HTTP call (this is the real one)
//   5. The fake clients at the bottom - for testing, no network
// ============================================================================

using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RunbookCopilot;

// ---------------------------------------------------------------------------
// The data shapes. These are just containers, like DTOs. No logic inside.
// ---------------------------------------------------------------------------

/// <summary>
/// The model ASKS us to run a function. This is that request.
///
/// Important: the model cannot run anything itself. It only sends text.
/// Our C# code reads this and decides whether to run the function.
///
/// Id            - a reference number. We must send it back with the answer.
/// Name          - which function, for example "get_service_status".
/// ArgumentsJson - the parameters, as a JSON string, for example {"service":"billing-api"}.
/// </summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// One function that we ALLOW the model to ask for.
///
/// We send this list to the model so it knows what exists.
/// If a function is not in this list, the model cannot ask for it.
///
/// Description      - plain English. The model reads this to choose.
/// ParametersSchema - JSON Schema describing the parameters.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersSchema);

/// <summary>
/// One message in the conversation. We send a LIST of these.
///
/// Role is one of:
///   "system"    - our rules for the model ("only use these passages")
///   "user"      - the question from the person
///   "assistant" - something the model said before
///   "tool"      - the result of a function we ran
///
/// ToolCallId - only for role "tool". It says WHICH ToolCall this answers.
/// ToolCalls  - only for role "assistant". The functions the model asked for.
/// </summary>
public sealed record ChatMessage(string Role, string Content, string? ToolCallId = null, IReadOnlyList<ToolCall>? ToolCalls = null);

/// <summary>
/// One reply from the model.
///
/// The model gives us EITHER an answer OR a list of functions it wants. Not both.
///   - Content has text, ToolCalls is empty  => it answered.
///   - ToolCalls has items                   => it wants us to run something first.
///
/// FinishReason - why it stopped. "stop" = finished normally.
///                "length" = it hit the maximum size and was cut off.
/// </summary>
public sealed record ChatTurn(string? Content, string FinishReason, IReadOnlyList<ToolCall> ToolCalls);

/// <summary>
/// The seam (the interface) between our application and the model.
///
/// Everything else in the app depends on THIS, not on HTTP.
/// That is why we can swap in a fake model for tests with no network and no API key.
/// There are 4 implementations in this file: 1 real, 3 fake.
/// </summary>
public interface IChatClient
{
    /// <param name="messages">The whole conversation so far. We resend all of it every time.</param>
    /// <param name="tools">Functions the model may ask for. Pass null when it should just answer.</param>
    Task<ChatTurn> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken);
}

/// <summary>
/// Builds the JSON body of a chat completions request.
/// <see cref="OpenAiChatClient"/> posts whatever this returns, and the trace command prints it,
/// so what you read in a trace is the request, not a second copy that can drift from it.
/// </summary>
public static class ChatRequest
{
    /// <summary>
    /// Temperature controls how "creative" the model is. Range is about 0 to 2.
    ///
    ///   Low  (0.1) - it picks the most likely words. Boring, repeatable. We want this.
    ///   High (1.5) - it picks surprising words. Good for poems. Bad for facts.
    ///
    /// We want the model to repeat what the runbook says. So we keep it low.
    /// Low temperature does NOT mean the answer is always correct. It only means
    /// the same question usually gives the same answer.
    /// </summary>
    public const double Temperature = 0.1;

    /// <summary>How the body is written on the wire.</summary>
    public static readonly JsonSerializerOptions Wire = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The same escaping as <see cref="Wire"/>, indented, for the trace command.</summary>
    public static readonly JsonSerializerOptions Readable = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    /// <summary>
    /// Builds the JSON object we POST. This is the most important method in the file.
    ///
    /// The result looks like this:
    ///
    ///   {
    ///     "model": "llama3.2",
    ///     "temperature": 0.1,
    ///     "messages": [ { "role": "system", ... }, { "role": "user", ... } ]
    ///   }
    ///
    /// That is all an "LLM application" sends. Nothing else.
    /// You can see it yourself, without sending anything:
    ///   dotnet run --project src/RunbookCopilot -- trace ask "your question"
    /// </summary>
    public static JsonObject Build(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools)
    {
        // Convert each ChatMessage into a JSON object, and put them all in an array.
        // The ORDER matters. The model reads them from first to last.
        var body = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = Temperature,
            ["messages"] = new JsonArray(messages.Select(MessageJson).ToArray())
        };

        // Only for the agent. When we pass tools, we are saying:
        // "you may ask me to run one of these functions instead of answering".
        // "auto" = the model decides. It can still just answer if it wants to.
        // When tools is null (the `ask` command), the model must answer. It has no other option.
        if (tools is { Count: > 0 })
        {
            body["tool_choice"] = "auto";
            body["tools"] = new JsonArray(tools.Select(ToolJson).ToArray());
        }

        return body;
    }

    /// <summary>
    /// Turns one C# ChatMessage into one JSON message.
    ///
    /// Notice: "content" is always just a string. Our system rules and a customer's
    /// pasted text are the SAME kind of field. Nothing marks one as trusted.
    /// That is why a system message is not a security wall.
    /// </summary>
    public static JsonNode MessageJson(ChatMessage message)
    {
        var node = new JsonObject
        {
            ["role"] = message.Role,
            ["content"] = message.Content ?? ""
        };

        // Only present on a "tool" message. It tells the model:
        // "this is the answer to the function call with THIS id".
        // Without it the model does not know which question we answered.
        if (!string.IsNullOrEmpty(message.ToolCallId))
        {
            node["tool_call_id"] = message.ToolCallId;
        }

        // Only present on an "assistant" message, when the model asked for functions.
        // We put its own request back into the conversation, so it remembers what it asked.
        if (message.ToolCalls is { Count: > 0 })
        {
            var calls = new JsonArray();
            foreach (var call in message.ToolCalls)
            {
                calls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        ["arguments"] = call.ArgumentsJson
                    }
                });
            }

            node["tool_calls"] = calls;
        }

        return node;
    }

    /// <summary>
    /// Describes ONE function to the model.
    ///
    /// The model receives only: the name, the description, and the parameter schema.
    /// It never receives our C# code. It cannot execute anything.
    /// It can only send back text saying "please run this one, with these arguments".
    /// </summary>
    public static JsonNode ToolJson(ToolDefinition tool)
    {
        return new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.ParametersSchema)
            }
        };
    }
}

/// <summary>
/// Builds the URL, and chooses the HTTP header that carries the API key.
///
/// Why this class exists: different companies host models at different URL shapes.
/// The JSON body is the same for all of them. Only the address and the key header change.
/// This class hides that difference, so the rest of the app never thinks about it.
///
/// Getting this URL wrong is one of the most common real bugs when people start,
/// so there is a test that locks both shapes: Azure_and_compatible_endpoints_build_different_urls.
/// </summary>
public sealed class ModelEndpoint
{
    /// <summary>The URL for asking a question. Always ends with /chat/completions.</summary>
    public required string ChatCompletions { get; init; }

    /// <summary>
    /// A DIFFERENT URL, for turning text into numbers instead of words.
    /// A chat model and an embedding model are two separate services. See OpenAiEmbedder below.
    /// </summary>
    public required string Embeddings { get; init; }

    public required string ChatModel { get; init; }

    public required string EmbeddingModel { get; init; }

    /// <summary>Which HTTP header carries the key. Azure and OpenAI use different header names.</summary>
    public required string? ApiKeyHeader { get; init; }

    /// <summary>The key itself. Null when no key is needed, for example a local Ollama.</summary>
    public required string? ApiKeyValue { get; init; }

    public static ModelEndpoint For(CopilotConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.Endpoint))
        {
            throw new InvalidOperationException("COPILOT_ENDPOINT is empty.");
        }

        var endpoint = config.Endpoint.TrimEnd('/');

        // --- Shape 1: Azure OpenAI ---------------------------------------
        // Azure is the unusual one. Three things are different:
        //   1. The model name is part of the PATH, not the JSON body.
        //      Azure calls it a "deployment" - a name YOU chose in the Azure portal.
        //   2. There is an api-version in the query string. It is required.
        //   3. The key goes in a header called "api-key".
        //
        // Result looks like:
        //   https://my-resource.openai.azure.com/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-10-21
        if (config.Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var version = Uri.EscapeDataString(config.ApiVersion);
            return new ModelEndpoint
            {
                ChatCompletions = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(config.ChatModel)}/chat/completions?api-version={version}",
                Embeddings = $"{endpoint}/openai/deployments/{Uri.EscapeDataString(config.EmbeddingModel)}/embeddings?api-version={version}",
                ChatModel = config.ChatModel,
                EmbeddingModel = config.EmbeddingModel,
                ApiKeyHeader = string.IsNullOrWhiteSpace(config.ApiKey) ? null : "api-key",
                ApiKeyValue = string.IsNullOrWhiteSpace(config.ApiKey) ? null : config.ApiKey
            };
        }

        // --- Shape 2: everything else -------------------------------------
        // "OpenAI compatible" means: it copies OpenAI's URL and JSON format.
        // Ollama, LM Studio, Groq, Together and many others all do this.
        // That is lucky for us: one client works for all of them.
        //
        //   Ollama on your laptop -> http://localhost:11434/v1/chat/completions
        //   OpenAI                -> https://api.openai.com/v1/chat/completions
        //
        // Here the model name goes in the JSON body, and the key goes in the
        // standard "Authorization: Bearer <key>" header.
        if (config.Provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase))
        {
            return new ModelEndpoint
            {
                ChatCompletions = $"{endpoint}/chat/completions",
                Embeddings = $"{endpoint}/embeddings",
                ChatModel = config.ChatModel,
                EmbeddingModel = config.EmbeddingModel,
                ApiKeyHeader = string.IsNullOrWhiteSpace(config.ApiKey) ? null : "Authorization",
                ApiKeyValue = string.IsNullOrWhiteSpace(config.ApiKey) ? null : $"Bearer {config.ApiKey}"
            };
        }

        throw new InvalidOperationException($"Provider '{config.Provider}' does not call a remote model.");
    }
}

/// <summary>
/// THE REAL ONE. This class talks to an actual model over the network.
///
/// Read this slowly. It is the centre of the whole project, and it is only
/// an HTTP client. If you have written a client for any REST API, you know this already.
///
/// There is no model inside this class. The model runs on another machine
/// (or in Ollama on your laptop). We only send text and read text.
/// </summary>
public sealed class OpenAiChatClient : IChatClient
{
    private readonly HttpClient _http;
    private readonly ModelEndpoint _endpoint;

    public OpenAiChatClient(HttpClient http, ModelEndpoint endpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    /// <summary>
    /// Send the conversation, get one reply.
    ///
    /// The whole method is three steps:
    ///   1. Build the JSON body.
    ///   2. POST it.
    ///   3. Dig the answer out of the JSON that comes back.
    ///
    /// Steps 1 and 2 are two lines. The rest of the method is step 3,
    /// because the reply JSON is deeply nested.
    /// </summary>
    public async Task<ChatTurn> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        // STEP 1: build the JSON we will send. See ChatRequest.Build above.
        var body = ChatRequest.Build(_endpoint.ChatModel, messages, tools);

        // STEP 2: send it. This is the only moment anything leaves this machine.
        var payload = await PostAsync(_endpoint.ChatCompletions, body, cancellationToken);

        // STEP 3: read the reply.
        //
        // The reply JSON looks like this:
        //
        //   {
        //     "choices": [
        //       {
        //         "finish_reason": "stop",
        //         "message": { "role": "assistant", "content": "eastus is paired with westus." }
        //       }
        //     ]
        //   }
        //
        // "choices" is an array because the API can return several different answers
        // if you ask for them. We always ask for one, so we take index [0].
        var choice = payload.RootElement.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        // Why the model stopped.
        //   "stop"       - it finished its sentence normally. Good.
        //   "length"     - it hit the size limit and was CUT OFF in the middle. The answer is incomplete.
        //   "tool_calls" - it wants us to run a function instead of answering.
        // We use TryGetProperty because not every server sends this field.
        var finish = choice.TryGetProperty("finish_reason", out var finishProperty)
            ? finishProperty.GetString() ?? "stop"
            : "stop";

        // The answer text. It can legitimately be missing or null when the model
        // decided to call a tool instead of answering, so we must not assume it is there.
        var content = message.TryGetProperty("content", out var contentProperty) && contentProperty.ValueKind == JsonValueKind.String
            ? contentProperty.GetString()
            : null;

        // Did the model ask us to run any functions?
        // If yes, "tool_calls" is an array. If it just answered, this field is absent.
        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            // It can be MORE THAN ONE. A real model may ask for two functions at the same time.
            // We saw llama3.2 do exactly that: get_service_status and get_runbook together.
            foreach (var call in toolCalls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                var arguments = function.GetProperty("arguments");

                // Careful here. The arguments arrive as a STRING containing JSON,
                // not as a real JSON object:
                //     "arguments": "{\"service\":\"billing-api\"}"
                // Some servers break the rule and send a real object instead,
                // so we handle both shapes. This is a real-world difference, not theory.
                var argumentsJson = arguments.ValueKind == JsonValueKind.String
                    ? arguments.GetString() ?? "{}"
                    : arguments.GetRawText();

                calls.Add(new ToolCall(
                    call.GetProperty("id").GetString() ?? "",
                    function.GetProperty("name").GetString() ?? "",
                    argumentsJson));
            }
        }

        // Remember: we only PARSED text here. Nothing was executed.
        // ToolCatalog.InvokeAsync in Agent.cs is what actually runs a function, later.
        return new ChatTurn(content, finish, calls);
    }

    /// <summary>
    /// The actual HTTP call. Ordinary C#, nothing about AI in here.
    ///
    /// Why POST and not GET? Three reasons:
    ///   1. A GET has no body, and our prompt is far too big for a URL.
    ///   2. URLs are written into logs, and the prompt contains private documents.
    ///   3. We are not reading something that exists. We are asking the server to
    ///      CREATE a new answer. Creating is POST. See notes/questions/001-why-post-not-get.md.
    /// </summary>
    private async Task<JsonDocument> PostAsync(string uri, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri);

        // The JSON goes in the BODY, not in the URL. This is the line that matters.
        request.Content = new StringContent(body.ToJsonString(ChatRequest.Wire), Encoding.UTF8, "application/json");

        // Attach the key, if we have one. A local Ollama needs no key, so this is optional.
        if (!string.IsNullOrEmpty(_endpoint.ApiKeyHeader) && !string.IsNullOrEmpty(_endpoint.ApiKeyValue))
        {
            if (_endpoint.ApiKeyHeader == "Authorization")
            {
                // The standard way: "Authorization: Bearer sk-abc123"
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(_endpoint.ApiKeyValue);
            }
            else
            {
                // Azure uses a custom header named "api-key". HttpClient does not know
                // that header, so we must add it without validation.
                request.Headers.TryAddWithoutValidation(_endpoint.ApiKeyHeader, _endpoint.ApiKeyValue);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            // The default .NET message here is confusing for a beginner.
            // We say which URL failed, because a wrong URL is the most common mistake.
            throw new InvalidOperationException($"Could not reach the model at {uri}. {ex.Message}", ex);
        }

        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Include part of the server's reply. The model APIs put the real reason in there,
            // for example "model not found" or "quota exceeded". We cut it to 400 characters
            // so a huge HTML error page does not fill your screen.
            var snippet = responseText.Length > 400 ? responseText[..400] : responseText;
            throw new InvalidOperationException($"Model request failed with {(int)response.StatusCode} from {uri}. {snippet}");
        }

        return JsonDocument.Parse(responseText);
    }
}

/// <summary>
/// A DIFFERENT kind of model. This is important and people mix it up.
///
///   Chat model      - text in, TEXT out.    "eastus is paired with westus."
///   Embedding model - text in, NUMBERS out. [0.12, -0.44, 0.91, ...]
///
/// Why do we want numbers? So we can measure MEANING as distance.
/// Two pieces of text with a similar meaning produce vectors that point in a similar
/// direction. So "paired region" and "failover partner" land close together, even
/// though they share no words. That is how search finds a passage that says the same
/// thing in different words.
///
/// It is a separate URL, a separate model, and a separate price.
/// This class is only used when you set COPILOT_EMBEDDING_MODEL. By default the app
/// uses HashingEmbedder in Embeddings.cs instead, which needs no network.
/// </summary>
public sealed class OpenAiEmbedder : IEmbedder
{
    private readonly HttpClient _http;
    private readonly ModelEndpoint _endpoint;

    public OpenAiEmbedder(HttpClient http, ModelEndpoint endpoint)
    {
        _http = http;
        _endpoint = endpoint;
    }

    public string Name => _endpoint.EmbeddingModel;

    /// <summary>One text in, one vector out. Just calls the batch version with a list of one.</summary>
    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vectors = await EmbedManyAsync(new[] { text }, cancellationToken);
        return vectors[0];
    }

    /// <summary>
    /// Many texts in, many vectors out.
    ///
    /// We send them in groups of 16 instead of one at a time. Why?
    /// Every HTTP call costs time (network delay) and sometimes money per call.
    /// 100 texts as 100 calls is slow. 100 texts as 7 calls is fast.
    /// This is called batching, and it is a normal API habit, not an AI idea.
    /// </summary>
    public async Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var results = new float[texts.Count][];
        const int batchSize = 16;
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var count = Math.Min(batchSize, texts.Count - offset);
            var input = new JsonArray();
            for (var i = 0; i < count; i++)
            {
                input.Add(texts[offset + i]);
            }

            var body = new JsonObject
            {
                ["model"] = _endpoint.EmbeddingModel,
                ["input"] = input
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint.Embeddings);
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_endpoint.ApiKeyHeader) && !string.IsNullOrEmpty(_endpoint.ApiKeyValue))
            {
                if (_endpoint.ApiKeyHeader == "Authorization")
                {
                    request.Headers.Authorization = AuthenticationHeaderValue.Parse(_endpoint.ApiKeyValue);
                }
                else
                {
                    request.Headers.TryAddWithoutValidation(_endpoint.ApiKeyHeader, _endpoint.ApiKeyValue);
                }
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new InvalidOperationException($"Could not reach embeddings at {_endpoint.Embeddings}. {ex.Message}", ex);
            }

            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var snippet = responseText.Length > 400 ? responseText[..400] : responseText;
                throw new InvalidOperationException(
                    $"Embedding request failed with {(int)response.StatusCode} from {_endpoint.Embeddings}. {snippet}");
            }

            // The reply is { "data": [ { "index": 0, "embedding": [0.12, -0.44, ...] }, ... ] }
            //
            // We must use "index", not the loop position. The server is allowed to return
            // the results in a different order than we sent them. Trusting the order here
            // would silently attach the wrong vector to the wrong chunk, and search would
            // return nonsense with no error message. Quiet bugs like this are the worst kind.
            using var document = JsonDocument.Parse(responseText);
            foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
            {
                var index = item.GetProperty("index").GetInt32();
                var vector = item.GetProperty("embedding").EnumerateArray().Select(number => (float)number.GetDouble()).ToArray();
                results[offset + index] = vector;
            }
        }

        return results;
    }
}

// ============================================================================
// The three FAKE clients below.
//
// They all implement IChatClient, so the rest of the app cannot tell the difference.
// That is the whole value of having an interface.
//
// None of them is a model. None of them understands language.
// They exist so the application can be run and tested with no API key and no network.
// ============================================================================

/// <summary>
/// A fake model so you can SEE the agent loop without an API key.
///
/// WARNING: this is NOT an LLM. It does not understand the question.
/// It follows fixed rules written in C#:
///   1. If the question contains a known service name -> ask for get_service_status.
///   2. Otherwise, or next time -> ask for search_runbooks.
///   3. After that -> stop and repeat whatever the tools returned.
///
/// So why is it useful? Because everything AROUND it is real: the loop, the
/// message list, the tool runner, the path check. You can test all of that
/// for free, offline, and get the same result every time.
///
/// What it CANNOT teach you is how a real model behaves. When we connected
/// llama3.2, it did something this fake never does: it asked for TWO functions
/// at once and never used search_runbooks at all.
/// The console prints "This is not an LLM" so you never confuse the two.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private readonly IReadOnlyList<string> _serviceNames;

    public ScriptedChatClient(IReadOnlyList<string> serviceNames) => _serviceNames = serviceNames;

    public Task<ChatTurn> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        // A real model reads the whole conversation. This fake one only looks at
        // three simple facts to decide what to do next.
        var user = messages.Last(message => message.Role == "user").Content;
        var alreadySearched = messages.Any(message => message.ToolCalls?.Any(call => call.Name == "search_runbooks") == true);
        var toolText = messages.Where(message => message.Role == "tool").Select(message => message.Content).ToArray();

        // Rule 1: first turn only, and only if the question names a service we know.
        if (tools is { Count: > 0 } && toolText.Length == 0)
        {
            var service = _serviceNames.FirstOrDefault(name => Mentions(user, name));
            if (service is not null)
            {
                return Task.FromResult(Tool("call-status", "get_service_status", JsonSerializer.Serialize(new { service }, JsonDefaults.Options)));
            }
        }

        // Rule 2: search the runbooks, but only once. Without this check it would
        // search forever until maxSteps stopped it.
        if (tools is { Count: > 0 } && !alreadySearched)
        {
            return Task.FromResult(Tool("call-search", "search_runbooks", JsonSerializer.Serialize(new { query = user }, JsonDefaults.Options)));
        }

        // Rule 3: stop. Return no tool calls, which tells AgentLoop the loop is finished.
        // A real model would now WRITE a sentence. This fake one just repeats the tool
        // output, and says so honestly in the first line.
        var answer = "Stand-in model. It is repeating tool results so the loop is visible."
            + Environment.NewLine
            + Environment.NewLine
            + string.Join(Environment.NewLine + Environment.NewLine, toolText);
        return Task.FromResult(new ChatTurn(answer, "stop", Array.Empty<ToolCall>()));
    }

    /// <summary>Small helper: build a reply that asks for one function instead of answering.</summary>
    private static ChatTurn Tool(string id, string name, string arguments) =>
        new(null, "tool_calls", new[] { new ToolCall(id, name, arguments) });

    /// <summary>
    /// Is this service name in the text, as a whole word?
    ///
    /// The regex guards stop a partial match. Without them, looking for "billing"
    /// would also match inside "billing-api-v2", and we would report the wrong service.
    /// </summary>
    private static bool Mentions(string text, string service) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9-]){System.Text.RegularExpressions.Regex.Escape(service)}(?![A-Za-z0-9-])",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

/// <summary>
/// Not a model at all. It is a WRAPPER around another client.
///
/// This design is called the decorator pattern. It takes any IChatClient,
/// writes down the request, then passes the call through to the real one.
///
///     AgentLoop  ->  RecordingChatClient  ->  ScriptedChatClient
///                    (writes it down)         (answers it)
///
/// The agent loop does not know it is being watched. It behaves exactly the same.
/// That is why the `trace agent` command shows you the REAL requests and not a
/// hand-written copy that could become out of date.
/// </summary>
public sealed class RecordingChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly string _model;
    private readonly List<JsonObject> _requests = new();

    public RecordingChatClient(IChatClient inner, string model)
    {
        _inner = inner;
        _model = model;
    }

    public IReadOnlyList<JsonObject> Requests => _requests;

    public Task<ChatTurn> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        // Write it down...
        _requests.Add(ChatRequest.Build(_model, messages, tools));
        // ...then let the real client do the work. We change nothing.
        return _inner.CompleteAsync(messages, tools, cancellationToken);
    }
}

/// <summary>
/// The simplest fake. It ignores the question and always returns the same text.
///
/// Used only in tests, when we need to control exactly what the "model" said.
/// For example, Model_json_wrapped_in_a_fence_still_validates gives it JSON wrapped
/// in a markdown fence, to prove our parser removes the fence. Real models really do
/// that, and we must not need a network call to test for it.
/// </summary>
public sealed class StubChatClient : IChatClient
{
    private readonly string _content;

    public StubChatClient(string content) => _content = content;

    public Task<ChatTurn> CompleteAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken) =>
        Task.FromResult(new ChatTurn(_content, "stop", Array.Empty<ToolCall>()));
}
