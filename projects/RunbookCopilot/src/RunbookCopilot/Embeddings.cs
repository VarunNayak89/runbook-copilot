// ============================================================================
// Embeddings.cs - turning text into numbers, so we can measure MEANING.
//
// The problem: a keyword search cannot see that these two sentences mean the same thing:
//     "Which region is paired with eastus?"
//     "What is the failover partner for eastus?"
// They share almost no words. Keyword search scores them as unrelated.
//
// The idea: convert each text into a list of numbers, called a VECTOR.
// Texts with similar meaning get vectors pointing in a similar DIRECTION.
// Then "find similar text" becomes "find vectors pointing the same way" - just maths.
//
// A vector of 384 numbers is a point in 384-dimensional space. You cannot picture that,
// and you do not need to. The only operation we use is "are these pointing the same way?"
// ============================================================================

namespace RunbookCopilot;

/// <summary>
/// The seam for embedding, exactly like IChatClient is the seam for chat.
///
/// Two implementations exist:
///   HashingEmbedder (below)      - fake, offline, free, weak
///   OpenAiEmbedder (in Chat.cs)  - real neural model, needs network, costs money, good
///
/// Everything else depends on this interface, so swapping them changes nothing else.
/// This is the normal C# habit you already use. It is not an AI idea.
/// </summary>
public interface IEmbedder
{
    string Name { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken);

    Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}

public static class VectorMath
{
    /// <summary>
    /// Cosine similarity: how much do two vectors point in the same DIRECTION?
    ///
    /// Result:
    ///     1.0  = same direction          (very similar meaning)
    ///     0.0  = at right angles         (unrelated)
    ///    -1.0  = opposite directions
    ///
    /// Why direction and not distance? Because length means "how much text", and
    /// direction means "about what". A long paragraph and a short sentence about the
    /// same topic have very different lengths but similar directions. We want the topic.
    ///
    /// The maths: dot product, divided by both lengths. Dividing by the lengths is what
    /// removes the size and leaves only the angle.
    /// </summary>
    public static double Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("Vectors must have the same number of dimensions.");
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        // A vector of all zeros has no direction, and dividing by zero would give NaN.
        // That happens for empty text, or text whose words all cancelled out.
        // Return 0 ("unrelated") instead of crashing.
        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }
}

/// <summary>
/// A FAKE embedder, so the app runs with no download and no API key.
///
/// How it works, in one sentence: every word is hashed to pick one slot out of 384,
/// and that slot is increased or decreased by 1.
///
///   "failover" -> hash -> slot 97,  sign +  ->  vector[97] += 1
///   "region"   -> hash -> slot 12,  sign -  ->  vector[12] -= 1
///
/// Two texts with many shared words end up changing the same slots, so their vectors
/// point in a similar direction. That is enough for the pipeline to work.
///
/// BE HONEST ABOUT WHAT THIS IS NOT.
/// A real neural embedder was TRAINED on language, so it knows that "paired region"
/// and "failover partner" mean the same thing. This one does not. It only counts words
/// in a clever way. It is closer to keyword matching than to understanding.
///
/// So why keep it?
///   1. The app runs offline, instantly, for free.
///   2. It gives the same answer every time, so tests are reliable.
///   3. It shows you the SHAPE of the pipeline without hiding it behind a download.
///
/// When you say "I used embeddings" in an interview, say WHICH kind. This one is
/// hashing. Set COPILOT_EMBEDDING_MODEL to use a real one instead.
/// </summary>
public sealed class HashingEmbedder : IEmbedder
{
    private readonly int _dimensions;

    public HashingEmbedder(int dimensions = 384)
    {
        if (dimensions < 32)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions));
        }

        _dimensions = dimensions;
    }

    public string Name => $"hashing-{_dimensions}";

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var vector = new float[_dimensions];
        foreach (var token in Tokenizer.Tokens(text))
        {
            var hash = Fnv1a(token);

            // Which of the 384 slots does this word belong to?
            // The % operator folds any hash value into the range 0..383.
            var bucket = (int)(hash % (uint)_dimensions);

            // Add 1 or subtract 1, decided by the top bit of the same hash.
            //
            // Why bother with a sign? Because 384 slots must hold thousands of different
            // words, so two unrelated words WILL share a slot. That is called a collision.
            // With random plus and minus, colliding words tend to cancel each other out
            // instead of always adding up and creating a fake match.
            var sign = (hash & 0x8000_0000) == 0 ? 1f : -1f;

            vector[bucket] += sign;
        }

        return Task.FromResult(vector);
    }

    public async Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            vectors[i] = await EmbedAsync(texts[i], cancellationToken);
        }

        return vectors;
    }

    /// <summary>
    /// FNV-1a, a small, fast, well-known hash function.
    ///
    /// We do NOT use string.GetHashCode(). That one is deliberately randomised per
    /// process in .NET, so the same word would hash differently after a restart, and
    /// your saved vectors would stop matching. FNV-1a gives the same answer forever,
    /// on any machine. The Python port uses this identical function for that reason.
    ///
    /// This is a plain hash, not cryptography. We only need "spread words evenly".
    /// </summary>
    private static uint Fnv1a(string text)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        foreach (var ch in text)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash;
    }
}
