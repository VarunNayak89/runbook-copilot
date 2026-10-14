# Day 4 — Structured output and validation

Concept note: [02-prompts-and-structured-output.md](../02-prompts-and-structured-output.md)
Code: [`Extraction.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Extraction.cs) · Evidence: [lab-6-transcript.md](../my-answers/lab-6-transcript.md)

---

## Technology used

| Thing | What it is | Where |
| --- | --- | --- |
| Deserialisation | `JsonSerializer.Deserialize<T>` with camelCase policy | `TenantRequestReader.Read` |
| Fence stripping | manual string surgery before parsing | `ExtractJsonObject` |
| Validation | hand-rolled allow-list, error list not exceptions | `TenantRules.Validate` |
| Regex baseline | `System.Text.RegularExpressions` | `PatternExtractor` |
| Date parsing | `DateOnly.TryParse` | `TenantRules.Validate` |
| Prompting | plain instruction + "Return JSON only" | `ModelExtractor` |

This is the **most useful LLM pattern in business software** — not chat. Turn a human sentence into an object your existing code already knows how to handle.

---

## The shape of the pipeline

```
sentence ──┬── PatternExtractor (regex)    ──┐
           │                                 ├──> json string ──> ExtractJsonObject
           └── ModelExtractor  (LLM)       ──┘                          │
                                                                        v
                                                          TenantRequestReader.Read
                                                                        │
                                                                        v
                                                             TenantRules.Validate
                                                                   /        \
                                                            Accepted      Rejected
                                                            exit 0        exit 1
```

**Two front ends, one validator.** The key design property: `TenantRules` does not know and must not know where the JSON came from. Swapping the extractor can never weaken the checks.

---

## `PatternExtractor` — the baseline nobody writes

```csharp
var nameMatch = Regex.Match(sentence, @"\bfor\s+([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
var dateMatch = Regex.Match(sentence, @"\d{4}-\d{2}-\d{2}");
return new TenantRequest
{
    Name        = nameMatch.Success ? nameMatch.Groups[1].Value : "",
    Region      = TenantRules.Regions.FirstOrDefault(r => sentence.Contains(r, StringComparison.OrdinalIgnoreCase)) ?? "",
    Environment = TenantRules.Environments.FirstOrDefault(e => Regex.IsMatch(sentence, $@"\b{e}\b", RegexOptions.IgnoreCase)) ?? "",
    IsTrial     = sentence.Contains("trial", StringComparison.OrdinalIgnoreCase),
    ExpiresOn   = dateMatch.Success ? dateMatch.Value : null
};
```

**Why this exists is an engineering habit, not a fallback.**

Before adding a model, ask: *how well does boring code do?* Often the answer is "well enough", and you have saved an API bill, a network dependency, and a source of non-determinism.

The right question is never "can AI do this". It is **"is AI better than the simple thing, and by how much?"** Most AI tutorials skip the baseline entirely.

Its limits are honest: `"Contoso needs a dev box in eastus"` defeats it — there is no `for X`. That is precisely where a model earns its place.

Note `Region` and `Environment` are matched **against `TenantRules`' own arrays**. One source of truth, reused by the regex, the prompt, and the validator.

---

## `ModelExtractor` — the prompt

```csharp
var system = """
    Extract one tenant request as a JSON object with fields name, region, environment, isTrial, expiresOn.
    region must be eastus, westus, or westeurope.
    environment must be dev, stg, or prd.
    isTrial is true only when the sentence describes a trial.
    expiresOn is yyyy-MM-dd when a date is present, otherwise null.
    Return JSON only.
    """;

var turn = await chat.CompleteAsync(
    new[] { new ChatMessage("system", system), new ChatMessage("user", sentence) },
    tools: null,
    cancellationToken);

return TenantRequestReader.ExtractJsonObject(turn.Content ?? "");
```

`tools: null` — this is a one-shot transform, not an agent. No loop, one API call.

`"Return JSON only"` is there because models are trained to be helpful and will happily write *"Sure! Here is the JSON you asked for:"* first.

---

## `ExtractJsonObject` — the unglamorous function you will always write

```csharp
var trimmed = text.Trim();

if (trimmed.StartsWith("```", StringComparison.Ordinal))
{
    var firstNewline = trimmed.IndexOf('\n');          // drop ``` or ```json — length varies
    if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..];

    var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
    if (fence >= 0) trimmed = trimmed[..fence];
}

var start = trimmed.IndexOf('{');
var end   = trimmed.LastIndexOf('}');
if (start >= 0 && end > start) return trimmed[start..(end + 1)];

return trimmed.Trim();
```

Handles the real output you get despite the instruction:

````
Sure! Here is the tenant request:
```json
{"name":"Contoso","region":"eastus"}
```
Let me know if you need anything else!
````

Details worth noticing:

- `IndexOf('\n')` rather than a fixed offset — the fence may be ` ``` ` or ` ```json `
- `LastIndexOf('}')`, not `IndexOf` — so nested objects survive
- No brace at all → return as-is and let `JsonException` produce a clear error

Test: `Model_json_wrapped_in_a_fence_still_validates`, using `StubChatClient` to supply exactly that broken output with no network.

---

## `TenantRules.Validate` — and its limit

```csharp
public static readonly string[] Regions      = ["eastus", "westus", "westeurope"];
public static readonly string[] Environments = ["dev", "stg", "prd"];

public static IReadOnlyList<string> Validate(TenantRequest request)
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Name is required.");
    if (!Regions.Contains(request.Region, StringComparer.OrdinalIgnoreCase)) errors.Add(...);
    if (!Environments.Contains(request.Environment, StringComparer.OrdinalIgnoreCase)) errors.Add(...);
    if (request.IsTrial && string.IsNullOrWhiteSpace(request.ExpiresOn)) errors.Add("A trial requires ExpiresOn.");
    if (!string.IsNullOrWhiteSpace(request.ExpiresOn) && !DateOnly.TryParse(request.ExpiresOn, out _)) errors.Add(...);
    return errors;
}
```

Design choices:

- returns a **list** rather than throwing — the user sees every problem at once
- empty list = valid; no separate bool
- `OrdinalIgnoreCase` — `"EastUS"` should pass
- mixes **format** rules (is it a date) with **business** rules (a trial needs an expiry)

### The finding that matters

Expected behaviour for `region: mars`: the model returns `"mars"`, the validator rejects it, exit 1.

Measured behaviour, **4 runs out of 4** at temperature 0.1:

```json
{ "name": "Fabrikam", "region": "westeurope", "environment": "prd", "isTrial": false, "expiresOn": null }
```

**Accepted. Exit code 0.**

The model did not violate the allow-list. It **silently substituted** a value that satisfies it.

| An allow-list catches | An allow-list does not catch |
| --- | --- |
| a region that does not exist | a plausible region that was never requested |
| a date that is not a date | a date invented out of nothing |
| a missing required field | a field quietly filled in |

`Validator_rejects_an_unknown_region` passes and is correct. It proves the validator rejects `mars` **when handed `mars`**. It cannot prove the model will ever hand it `mars`.

### The missing check

Nothing compares the extracted value back to **the input sentence**. That is the gap, and it is a different *class* of check — not a stricter schema, not a better prompt:

```csharp
// sketch: grounding check
if (!sentence.Contains(request.Region, StringComparison.OrdinalIgnoreCase))
    errors.Add($"Region '{request.Region}' does not appear in the input. Not inferring it.");
```

This is **output grounding**, and in the production vocabulary it is an *output guardrail*. Lab 7.

The smaller version of the same behaviour: given `"...for Contoso..."`, the regex returns `"Contoso"`; the model returned `"Contoso Trial"`. Harmless here. Same instinct that produced `westeurope`.

---

## What you would use in production

| This project | Production |
| --- | --- |
| "Return JSON only" in prose | **Structured Outputs / JSON mode** — provider enforces a JSON Schema, so malformed JSON becomes impossible |
| hand-written `Validate` | **FluentValidation**, or DataAnnotations |
| manual fence stripping | mostly unnecessary once JSON mode is on — keep it as a fallback |
| no grounding check | compare every extracted value against the source text |
| no confidence signal | ask for a `confidence` field, route low values to a human |
| accept → act | **propose → validate → human approval → act** for anything that writes |
| no retry | on validation failure, send the errors back and let it correct itself |

**Structured Outputs is the big one.** Most providers now accept a JSON Schema and guarantee conforming output. It removes a whole class of parsing bugs.

What it does **not** remove: the `mars` problem. Schema conformance says the shape is right. It says nothing about whether the value is *true*. That distinction is the thing to understand here.

---

## Interview questions on this code

1. Why does a regex extractor exist alongside the model?
2. Why return a list of errors instead of throwing?
3. The prompt lists the allowed regions. Why validate again in C#?
4. Your validator has a passing test, and a wrong value got through. Explain.
5. What check is missing, and which guardrail layer does it belong to?
6. Would Structured Outputs / JSON mode have prevented the `mars` result?
7. `finish_reason` was `"length"`. What happens to your JSON parse?

*(6 and 7 are the ones that separate people who have read about this from people who have run it.)*

---

## Try it

```powershell
$env:COPILOT_PROVIDER="OpenAICompatible"; $env:COPILOT_ENDPOINT="http://localhost:11434/v1"; $env:COPILOT_CHAT_MODEL="llama3.2"

dotnet run --project src/RunbookCopilot -- extract "Create a tenant for Fabrikam in mars, environment prod"
```

Run it four times. Note the region, and note the exit code.
