// ============================================================================
// Extraction.cs - turning a human sentence into an object your code can use.
//
// This is called STRUCTURED OUTPUT, and it is the most useful LLM pattern in
// business software. Not chat. This.
//
//   in : "Create a trial tenant for Contoso in eastus, environment dev, ending 2026-11-01"
//   out: TenantRequest { Name="Contoso", Region="eastus", Environment="dev", ... }
//
// Once it is an object, your EXISTING code can use it. The AI part stops at the
// object boundary. Everything after that is the software you already know how to write.
//
// THE RULE: a prompt is a wish. A validator is the product.
// You ASK the model for the right shape. You CHECK it in C#. Always both.
//
// But read the warning at TenantRules below. Checking is necessary and it is not enough.
// ============================================================================

using System.Text.Json;
using System.Text.RegularExpressions;

namespace RunbookCopilot;

/// <summary>The object we want. A plain C# class, nothing special about it.</summary>
public sealed class TenantRequest
{
    public string Name { get; set; } = "";

    public string Region { get; set; } = "";

    public string Environment { get; set; } = "";

    public bool IsTrial { get; set; }

    public string? ExpiresOn { get; set; }
}

/// <summary>
/// The validator. This runs on the model's output before anything else happens.
///
/// It is the same instinct you already have for a web API: never trust the caller,
/// check the values against the list you actually support. Here the caller is a model.
///
/// ---------------------------------------------------------------------------
/// IMPORTANT - WHAT THIS CANNOT DO. Read this part twice.
///
/// We tested it. We asked:
///     extract "Create a tenant for Fabrikam in mars, environment prod"
///
/// We expected: the model returns "mars", this validator rejects it. Good story.
///
/// What really happened, 4 times out of 4:
///     the model returned "region": "westeurope"  ->  ACCEPTED, exit code 0.
///
/// The model did not break the rule. It quietly REPLACED the bad input with a legal
/// value, and the validator had nothing to complain about.
///
/// So be precise about what an allow-list buys you:
///     it catches MALFORMED output     (a region that does not exist)
///     it does NOT catch CONFIDENT INVENTION (a wrong value that looks correct)
///
/// The missing check is not a better prompt and not a stricter schema. It is that
/// nothing here compares the value back to the SENTENCE it came from. Fixing that
/// is lab 7. Full transcript: notes/my-answers/lab-6-transcript.md
/// ---------------------------------------------------------------------------
/// </summary>
public static class TenantRules
{
    // The allow-lists. Defined ONCE, used in two places: the prompt text we send to
    // the model, and the check below. One source of truth, so they cannot disagree.
    public static readonly string[] Regions = ["eastus", "westus", "westeurope"];

    public static readonly string[] Environments = ["dev", "stg", "prd"];

    /// <summary>
    /// Returns a list of problems. An EMPTY list means the request is acceptable.
    ///
    /// We collect all the errors instead of returning on the first one, so the user
    /// sees everything that is wrong in one go rather than fixing them one at a time.
    /// </summary>
    public static IReadOnlyList<string> Validate(TenantRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("Name is required.");
        }

        // The allow-list check. The prompt already asked for these three regions.
        // We check again here because asking is not the same as enforcing.
        if (!Regions.Contains(request.Region, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Region '{request.Region}' is not allowed. Allowed: {string.Join(", ", Regions)}.");
        }

        if (!Environments.Contains(request.Environment, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add($"Environment '{request.Environment}' is not allowed. Allowed: {string.Join(", ", Environments)}.");
        }

        // A business rule, not a format rule. A trial must end on some date, or it is
        // not a trial - it is a free account forever. The model often forgets this,
        // because the sentence does not always mention a date.
        if (request.IsTrial && string.IsNullOrWhiteSpace(request.ExpiresOn))
        {
            errors.Add("A trial requires ExpiresOn.");
        }

        // Models write dates in many formats: "1st Nov 2026", "11/01/2026", "next month".
        // We asked for yyyy-MM-dd. Here we make sure we actually got a real date.
        if (!string.IsNullOrWhiteSpace(request.ExpiresOn) && !DateOnly.TryParse(request.ExpiresOn, out _))
        {
            errors.Add("ExpiresOn must be a calendar date, yyyy-MM-dd.");
        }

        return errors;
    }
}

/// <summary>
/// Extraction WITHOUT any model, using regular expressions.
///
/// Two reasons this exists:
///   1. The app runs offline, with no API key. You can learn without paying.
///   2. It is a BASELINE. Before you add a model, ask: how well does boring code do?
///      Often the answer is "well enough", and then you have saved yourself an API bill,
///      a network dependency, and a source of random behaviour.
///
/// This is a real engineering habit and most AI tutorials skip it. The question is
/// never "can AI do this". It is "is AI better than the simple thing, and by how much".
///
/// Its limits are obvious: it needs the sentence to be shaped the way it expects.
/// "Contoso needs a dev box in eastus" would defeat it. That is where a model wins.
/// </summary>
public static class PatternExtractor
{
    public static TenantRequest Extract(string sentence)
    {
        var nameMatch = Regex.Match(sentence, @"\bfor\s+([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
        var dateMatch = Regex.Match(sentence, @"\d{4}-\d{2}-\d{2}");
        return new TenantRequest
        {
            Name = nameMatch.Success ? nameMatch.Groups[1].Value : "",
            Region = TenantRules.Regions.FirstOrDefault(region => sentence.Contains(region, StringComparison.OrdinalIgnoreCase)) ?? "",
            Environment = TenantRules.Environments.FirstOrDefault(environment => Regex.IsMatch(sentence, $@"\b{environment}\b", RegexOptions.IgnoreCase)) ?? "",
            IsTrial = sentence.Contains("trial", StringComparison.OrdinalIgnoreCase),
            ExpiresOn = dateMatch.Success ? dateMatch.Value : null
        };
    }
}

/// <summary>
/// Extraction WITH a model. Same job as PatternExtractor, same output, different method.
///
/// Both paths produce a JSON string. Both then go through the SAME TenantRules.Validate.
/// That is the design: the validator does not care, and must not care, where the JSON
/// came from. Swapping the extractor never weakens the checks.
/// </summary>
public static class ModelExtractor
{
    public static async Task<string> CompleteAsync(IChatClient chat, string sentence, CancellationToken cancellationToken)
    {
        // The instructions. Notice we list the allowed values here as well.
        // This REDUCES bad output. It does not prevent it. The validator is the wall.
        //
        // "Return JSON only" is here because models love to be helpful and add
        // "Sure! Here is the JSON you asked for:" before the object.
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
    }
}

/// <summary>
/// Cleaning up what the model actually sent, before we try to parse it.
///
/// This class is unglamorous and you will write something like it in every real
/// project that asks a model for JSON.
/// </summary>
public static class TenantRequestReader
{
    /// <summary>
    /// Find the JSON object inside the model's reply.
    ///
    /// We said "Return JSON only". Models frequently reply like this anyway:
    ///
    ///     Sure! Here is the tenant request:
    ///     ```json
    ///     {"name":"Contoso","region":"eastus"}
    ///     ```
    ///     Let me know if you need anything else!
    ///
    /// JsonSerializer would fail on that immediately. So we dig the object out first.
    /// This is not us being defensive for fun: there is a test called
    /// Model_json_wrapped_in_a_fence_still_validates because this happens constantly.
    /// </summary>
    public static string ExtractJsonObject(string text)
    {
        var trimmed = text.Trim();

        // Step 1: remove a markdown code fence, if there is one.
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            // Drop the first line. It may be ``` or ```json, so we cannot assume a length.
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            // Drop everything from the closing fence onward.
            var fence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                trimmed = trimmed[..fence];
            }
        }

        // Step 2: take everything between the FIRST { and the LAST }.
        // This also removes any chatty sentences before or after the object.
        // LastIndexOf, not IndexOf, so that nested objects survive intact.
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        // No braces at all. Return what we have and let the parser produce a clear error.
        return trimmed.Trim();
    }

    /// <summary>
    /// Parse the JSON into our object. Throws JsonException when the text is not valid,
    /// and the caller in Program.cs turns that into "Rejected" plus exit code 1.
    /// </summary>
    public static TenantRequest Read(string json) =>
        JsonSerializer.Deserialize<TenantRequest>(json, JsonDefaults.Options)
        ?? throw new JsonException("Model output did not contain a tenant request.");
}
