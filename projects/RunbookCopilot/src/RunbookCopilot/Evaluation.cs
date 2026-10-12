// ============================================================================
// Evaluation.cs - measuring whether the search actually works.
//
// WHY THIS FILE MATTERS MORE THAN IT LOOKS:
//
// Anyone can run an AI demo once, get a good answer, and feel successful.
// That is not evidence. The next question may fail and you would not know.
//
// So we write down a set of questions with the file that SHOULD answer each one,
// and we score it. Now a change can be measured instead of guessed:
//     "I made the chunks smaller. Did the score go up or down?"
//
// This is the same habit as unit testing. It is cheap, it is fast, it is boring,
// and it is the thing that separates engineering from demos.
//
// IMPORTANT LIMIT: this file tests the SEARCH only. No model is called.
// It can tell you the right page was found. It cannot tell you the model wrote
// a good sentence. Those are two different measurements and people confuse them.
// ============================================================================

namespace RunbookCopilot;

/// <summary>
/// One test case, loaded from content/eval/questions.json.
/// Question = what to ask. ExpectedSource = which file must appear in the results.
/// </summary>
public sealed class EvalQuestion
{
    public string Question { get; set; } = "";

    public string ExpectedSource { get; set; } = "";
}

public sealed record EvalCaseResult(string Question, string ExpectedSource, bool Pass, string TopSource, double Score);

public sealed record EvalReport(IReadOnlyList<EvalCaseResult> Cases)
{
    public int Passed => Cases.Count(result => result.Pass);

    /// <summary>
    /// RECALL@3 - the number the eval command prints.
    ///
    /// Meaning: of all the questions, what FRACTION had the correct file somewhere
    /// in the top 3 results?
    ///
    ///   recall  = "did we find it at all?"
    ///   @3      = "we only looked at the top 3"
    ///
    /// Why 3 and not 1? Because we send several passages to the model anyway.
    /// The right page does not have to be first. It has to be PRESENT.
    ///
    /// WHAT 1.00 DOES NOT MEAN. This is the part to remember:
    ///   - it does not mean the other 2 results were useful. They may be noise.
    ///   - it does not mean the model will write a correct sentence.
    ///   - on our failover question, the right file is first AND incidents.md,
    ///     which answers nothing, still rides along in third place.
    ///
    /// A perfect retrieval score with an invented answer is still a failed product.
    /// </summary>
    public double Recall => Cases.Count == 0 ? 0 : (double)Passed / Cases.Count;
}

public sealed class RetrievalEval
{
    private readonly HybridSearch _search;

    public RetrievalEval(HybridSearch search) => _search = search;

    /// <summary>
    /// Run every question and score it. No model is called anywhere in this method,
    /// which is why the eval is free, fast, and gives the same answer every time.
    /// That means you can run it on every change, like a unit test.
    /// </summary>
    public async Task<EvalReport> RunAsync(IReadOnlyList<EvalQuestion> questions, int topK, CancellationToken cancellationToken)
    {
        var results = new List<EvalCaseResult>();
        foreach (var question in questions)
        {
            var hits = await _search.SearchAsync(question.Question, topK, cancellationToken);

            // .Any() - the expected file must be ANYWHERE in the top 3, not necessarily first.
            // That is what the "recall" in recall@3 means: did we find it at all.
            var pass = hits.Any(hit => hit.Chunk.Source.Equals(question.ExpectedSource, StringComparison.OrdinalIgnoreCase));

            // We also record the top hit, only so the console can show it to you.
            // It is not used for scoring. Looking at it is how you notice that a test
            // "passed" while a completely unrelated file was ranked first.
            var top = hits.FirstOrDefault();
            results.Add(new EvalCaseResult(
                question.Question,
                question.ExpectedSource,
                pass,
                top?.Chunk.Source ?? "(none)",
                top?.Score ?? 0));
        }

        return new EvalReport(results);
    }
}

/// <summary>
/// The SECOND kind of evaluation: grading the model's written answer.
///
/// RetrievalEval above asks "did we find the right page?". This asks
/// "did the model use it properly?". You need both, and they are separate jobs.
///
/// This one is deliberately crude: does the answer contain the file name (a citation),
/// and does it contain the fact we expected? Two string checks.
///
/// Crude is still useful. It catches the two most common failures: an answer with no
/// citation, and an answer that quietly dropped the number you needed.
///
/// What it cannot catch is a fluent, well-cited sentence that is subtly wrong - like
/// the time our model turned "do not fail over WHILE a lock is held" into "a lock IS
/// in place". Only a human reading the passages catches that. Grading answers properly
/// is a genuinely hard, unsolved problem, and anyone who tells you otherwise is selling
/// something.
///
/// Unused until you configure a model, because there is no answer to grade without one.
/// </summary>
public static class AnswerRubric
{
    public static IReadOnlyList<string> Check(string answer, string citationFile, string requiredPhrase)
    {
        var failures = new List<string>();
        if (!answer.Contains(citationFile, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Missing citation {citationFile}.");
        }

        if (!answer.Contains(requiredPhrase, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"Missing required phrase '{requiredPhrase}'.");
        }

        return failures;
    }
}
