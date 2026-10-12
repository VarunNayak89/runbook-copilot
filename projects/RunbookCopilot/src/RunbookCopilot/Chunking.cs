// ============================================================================
// Chunking.cs - cutting documents into small pieces.
//
// WHY do we cut them up? Two reasons:
//
//   1. SIZE. We cannot send the whole document folder in every request.
//      It would be slow and expensive, and there is a maximum size.
//
//   2. ACCURACY. If we sent a 50 page document, the one useful sentence would be
//      surrounded by 49 pages of unrelated text. Search works better on small pieces,
//      and the model answers better when the prompt is mostly relevant.
//
// So: cut the document into pieces, search the pieces, send only the best few.
// A piece is called a CHUNK. In LangChain this job is done by a "text splitter".
// ============================================================================

namespace RunbookCopilot;

/// <summary>
/// One piece of a document.
///
/// Id      - unique name, like "failover.md#0". Used to recognise the same chunk
///           when two different searches both return it.
/// Source  - the file name. We MUST keep this, otherwise the answer cannot cite anything.
/// Heading - the markdown heading this text was under. Useful context for the model.
/// Text    - the actual words.
/// </summary>
public sealed record Chunk(string Id, string Source, string Heading, string Text);

/// <summary>
/// Cuts text into words, for the keyword search in Search.cs.
///
/// The rule is deliberately simple: keep letters and digits, lowercase everything,
/// treat everything else as a separator.
///
///   "eastus, westus."  ->  ["eastus", "westus"]
///
/// Why lowercase and strip punctuation? So that "eastus," with a comma and "Eastus"
/// and "eastus" all become the same word. Without this, the search would treat them
/// as three different terms and miss obvious matches.
///
/// NOTE: this is OUR word splitter, for the keyword index. It is NOT the same as the
/// "tokens" you pay for with a model. Those are decided by the model's own tokenizer.
/// Same word, two meanings. People confuse these constantly.
/// </summary>
public static class Tokenizer
{
    public static IReadOnlyList<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}

/// <summary>
/// Cuts a markdown file into chunks.
///
/// The strategy is in two steps:
///   1. Cut at every heading (#, ## or ###). A heading usually marks one topic,
///      so this keeps related sentences together. This is a good default for markdown.
///   2. If a section is STILL too long, cut it into fixed-size windows.
///
/// CHUNK SIZE IS A TRADE-OFF. There is no correct answer, only a measured one:
///
///   Small chunks - very focused, good match scores.
///                  BUT a fact can be cut away from the sentence that qualifies it.
///                  Example: "Rotate the key every 90 days" in one chunk and
///                  "unless the tenant is a trial" in the next. Now the answer is wrong.
///
///   Large chunks - keep the full context together.
///                  BUT they also carry unrelated sentences, which dilutes the match
///                  and costs more to send.
///
/// You choose the size by running the eval and looking at the score. Not by copying a blog.
/// Lab 3 asks you to set maxChars to 200 and watch what breaks.
/// </summary>
public sealed class MarkdownChunker
{
    private readonly int _maxChars;
    private readonly int _overlapChars;

    /// <param name="maxChars">Biggest chunk we allow, in characters.</param>
    /// <param name="overlapChars">
    /// How much text to REPEAT at the start of the next window.
    ///
    /// Why repeat anything? Imagine a fact sits exactly on the cut line:
    ///     ...the DNS TTL is | 60 seconds...
    /// Window 1 ends with "the DNS TTL is". Window 2 starts with "60 seconds".
    /// Neither window can answer the question. Both halves are useless.
    ///
    /// Overlap repeats the last 150 characters, so the complete sentence exists
    /// in at least one window. We waste a little space to avoid losing a fact.
    /// </param>
    public MarkdownChunker(int maxChars = 1000, int overlapChars = 150)
    {
        if (maxChars < 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChars));
        }

        if (overlapChars < 0 || overlapChars >= maxChars)
        {
            throw new ArgumentOutOfRangeException(nameof(overlapChars));
        }

        _maxChars = maxChars;
        _overlapChars = overlapChars;
    }

    /// <summary>Cut one markdown file into chunks.</summary>
    public IReadOnlyList<Chunk> Chunk(string source, string markdown)
    {
        // Normalise Windows line endings to \n first, so one rule works everywhere.
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var chunks = new List<Chunk>();
        string? heading = null;
        var body = new System.Text.StringBuilder();
        var index = 0;

        // Local function. Called every time we reach a new heading, and once at the end.
        // It takes whatever text we have collected and turns it into chunks.
        void Emit()
        {
            // Text before the first heading has no title. Give it one.
            var title = heading ?? "Introduction";
            var text = body.ToString().Trim();
            body.Clear();

            // Two headings in a row produce no text. Do not create an empty chunk.
            if (text.Length == 0)
            {
                return;
            }

            // Put the heading back INSIDE the chunk text.
            //
            // This matters. The heading is often the best clue about what the text means.
            // "DNS TTL" may appear only in the heading and never in the sentences below.
            // If we drop it, a search for "DNS TTL" cannot find this chunk.
            var combined = $"# {title}\n{text}";

            foreach (var window in Windows(combined))
            {
                chunks.Add(new Chunk($"{source}#{index}", source, title, window.Trim()));
                index++;
            }
        }

        foreach (var line in lines)
        {
            if (IsHeading(line))
            {
                Emit();                                 // finish the previous section...
                heading = line.TrimStart('#').Trim();   // ...and start a new one
            }
            else
            {
                body.AppendLine(line);
            }
        }

        Emit();   // do not forget the last section. Easy bug to miss.
        return chunks;
    }

    /// <summary>
    /// Step 2 of chunking: cut an over-long section into overlapping windows.
    ///
    /// With maxChars = 1000 and overlapChars = 150, the windows are:
    ///     window 1: characters    0 - 1000
    ///     window 2: characters  850 - 1850     <- 850, not 1000. It steps back 150.
    ///     window 3: characters 1700 - 2700
    ///
    /// Each window repeats the last 150 characters of the one before it.
    /// </summary>
    private IEnumerable<string> Windows(string combined)
    {
        // Most sections are small enough already. This is the normal case.
        if (combined.Length <= _maxChars)
        {
            yield return combined;
            yield break;
        }

        var start = 0;
        while (start < combined.Length)
        {
            // Math.Min stops us reading past the end of the string on the last window.
            var length = Math.Min(_maxChars, combined.Length - start);
            yield return combined.Substring(start, length);

            if (start + length >= combined.Length)
            {
                yield break;
            }

            // Move forward by LESS than a full window. The difference is the overlap.
            // The constructor guarantees overlapChars < maxChars, so this always moves
            // forward. If they were equal, start would never increase and this would
            // loop forever.
            start += _maxChars - _overlapChars;
        }
    }

    /// <summary>
    /// Is this line a heading? We accept #, ## and ### only.
    ///
    /// The trailing space is required: "# Title" is a heading, but "#hashtag" is not.
    /// We stop at ### because deeper levels usually produce chunks too small to be useful.
    /// </summary>
    private static bool IsHeading(string line) =>
        line.StartsWith("# ", StringComparison.Ordinal)
        || line.StartsWith("## ", StringComparison.Ordinal)
        || line.StartsWith("### ", StringComparison.Ordinal);
}
