// ============================================================================
// Search.cs - this file IS the "R" in RAG. Retrieval.
//
// This is the most important file in the project, and most people underestimate it.
//
// Remember the rule: if search picks the WRONG passages, the correct answer is not
// in the request at all. The model cannot rescue you. No prompt wording helps.
// So retrieval quality sets the ceiling for the whole application.
//
// We search in TWO different ways and then combine the results:
//
//   1. LexicalIndex (BM25) - matching WORDS.    Good at exact terms: "DNS TTL", "90 days".
//   2. VectorIndex         - matching MEANING.  Good at different words, same idea.
//
// Each one is blind where the other sees. Using both is called HYBRID SEARCH.
// ============================================================================

namespace RunbookCopilot;

/// <summary>A chunk together with how well it matched. Higher Score is better.</summary>
public sealed record ScoredChunk(Chunk Chunk, double Score);

/// <summary>
/// Search by MEANING, using the vectors from Embeddings.cs.
///
/// It compares the question's vector against every chunk's vector and keeps the closest.
///
/// Yes, this is a plain List, and yes, it checks every single chunk one by one.
/// For 16 chunks that is instant. For 10 million chunks it would be far too slow,
/// and you would use a vector database instead: Azure AI Search, pgvector, Qdrant.
/// Those products are this same idea, plus speed tricks and filters.
/// The IDEA is what you are learning here. The product name is just shopping.
/// </summary>
public sealed class VectorIndex
{
    private readonly List<(Chunk Chunk, float[] Vector)> _items = new();

    public void Add(Chunk chunk, float[] vector) => _items.Add((chunk, vector));

    /// <param name="topK">How many results to return. Keep only the best few.</param>
    public IReadOnlyList<ScoredChunk> Search(float[] query, int topK)
    {
        return _items
            .Select(item => new ScoredChunk(item.Chunk, VectorMath.Cosine(query, item.Vector)))
            .OrderByDescending(item => item.Score)
            .Take(topK)
            .ToArray();
    }
}

/// <summary>
/// Search by WORDS, using an algorithm called BM25.
///
/// BM25 is the standard keyword ranking formula. Elasticsearch, Lucene and most search
/// engines use it or something very close. It is 30 years old, not AI, and still excellent.
///
/// It scores a chunk higher when:
///   1. the chunk contains the search word MANY times            (term frequency)
///   2. the word is RARE across all chunks                       (inverse document frequency)
///   3. the chunk is SHORT                                       (length normalisation)
///
/// Point 2 is the clever part. The word "the" appears everywhere, so matching it means
/// nothing. The word "TTL" appears in one place, so matching it means a lot.
/// BM25 gives almost no credit for common words, automatically.
///
/// Point 3 stops a very long chunk from winning just because it contains more words.
/// </summary>
public sealed class LexicalIndex
{
    // Two tuning constants from the BM25 paper. These values are the usual defaults.
    // K1 controls how much repeating a word keeps helping (it stops helping quickly).
    // B  controls how strongly we punish long chunks. 0 = ignore length, 1 = full punish.
    // Do not change these to "improve" the score. Change the chunks instead.
    private const double K1 = 1.2;
    private const double B = 0.75;

    private readonly List<Chunk> _chunks = new();

    /// <summary>For each chunk: how many times each word appears in it.</summary>
    private readonly List<Dictionary<string, int>> _termFrequency = new();

    /// <summary>For each chunk: how many words it has in total. Used for point 3.</summary>
    private readonly List<int> _lengths = new();

    /// <summary>For each word: in HOW MANY chunks does it appear? This is what makes point 2 work.</summary>
    private readonly Dictionary<string, int> _documentFrequency = new();

    /// <summary>
    /// Add one chunk and update the statistics.
    ///
    /// We do this work ONCE when the app starts, not during a search. That is what an
    /// "index" means: pay the cost early so every later search is fast.
    /// </summary>
    public void Add(Chunk chunk)
    {
        var counts = new Dictionary<string, int>();

        // Note we index the Heading as well as the Text.
        // A search for "DNS TTL" must find the section whose heading is "DNS TTL",
        // even if those exact letters never appear in the sentences below it.
        foreach (var token in Tokenizer.Tokens(chunk.Heading + " " + chunk.Text))
        {
            counts[token] = counts.GetValueOrDefault(token) + 1;
        }

        // counts.Keys, not the token list: we count each word ONCE per chunk here.
        // This answers "in how many chunks does this word appear?", not "how often".
        foreach (var term in counts.Keys)
        {
            _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
        }

        _chunks.Add(chunk);
        _termFrequency.Add(counts);

        // Math.Max(1, ...) protects against an empty chunk. A length of 0 would later
        // be divided by, and produce infinity.
        _lengths.Add(Math.Max(1, counts.Values.Sum()));
    }

    public IReadOnlyList<ScoredChunk> Search(string query, int topK)
    {
        if (_chunks.Count == 0)
        {
            return Array.Empty<ScoredChunk>();
        }

        // Split the question into words, the same way we split the chunks.
        // Distinct(): asking "region region region" should not score triple.
        var queryTerms = Tokenizer.Tokens(query).Distinct().ToArray();
        var averageLength = _lengths.Average();

        var scored = new List<ScoredChunk>();
        for (var i = 0; i < _chunks.Count; i++)
        {
            double score = 0;
            foreach (var term in queryTerms)
            {
                // This chunk does not contain the word at all. It scores nothing for it.
                if (!_termFrequency[i].TryGetValue(term, out var tf))
                {
                    continue;
                }

                // --- the BM25 formula, in three parts ---

                // df = in how many chunks does this word appear?
                var df = _documentFrequency[term];

                // idf = "inverse document frequency" = how RARE and therefore how valuable.
                // A word in every chunk gives idf near 0. A word in one chunk gives a big idf.
                // The 0.5 values are smoothing, so the maths never divides by zero.
                var idf = Math.Log(1 + (_chunks.Count - df + 0.5) / (df + 0.5));

                // The denominator holds the length punishment.
                // _lengths[i] / averageLength is "how long is this chunk compared to normal".
                // A longer than average chunk makes the denominator bigger, so the score smaller.
                var denominator = tf + K1 * (1 - B + B * _lengths[i] / averageLength);

                // Put it together. More occurrences (tf) and rarer word (idf) = higher.
                // Because tf appears on the top AND the bottom, the 10th occurrence of a
                // word adds much less than the 2nd. That is the saturation K1 controls.
                score += idf * (tf * (K1 + 1)) / denominator;
            }

            // Chunks that matched nothing are dropped completely. No point ranking zeros.
            if (score > 0)
            {
                scored.Add(new ScoredChunk(_chunks[i], score));
            }
        }

        return scored
            .OrderByDescending(item => item.Score)
            .Take(topK)
            .ToArray();
    }
}

/// <summary>
/// Runs BOTH searches and combines the two result lists into one.
///
/// Why both? Because each one fails in a different way:
///
///   Question: "What is the DNS TTL after a regional failover?"
///     BM25   - strong. "TTL" is a rare word and appears in the right chunk.
///     Vector - weaker. A rare technical code is a blurry signal in a vector.
///
///   Question: "Which region is the failover partner for eastus?"
///     BM25   - weak. The runbook says "paired region", not "failover partner". No shared words.
///     Vector - strong. Similar meaning, so similar direction.
///
/// Using both means one can rescue the other. In LangChain this class is called
/// an EnsembleRetriever, and it does exactly the same arithmetic.
/// </summary>
public sealed class HybridSearch
{
    private readonly IEmbedder _embedder;
    private readonly VectorIndex _vectors;
    private readonly LexicalIndex _lexical;

    public HybridSearch(IEmbedder embedder, VectorIndex vectors, LexicalIndex lexical)
    {
        _embedder = embedder;
        _vectors = vectors;
        _lexical = lexical;
    }

    public string EmbeddingName => _embedder.Name;

    public async Task<IReadOnlyList<ScoredChunk>> SearchAsync(string query, int topK, CancellationToken cancellationToken)
    {
        // Ask each search for MORE results than we finally need (at least 8).
        // A chunk ranked 6th by one method and 7th by the other may win after combining.
        // If we only asked for the top 4 from each, we would never see it.
        var pool = Math.Max(topK, 8);

        // Turn the QUESTION into a vector too, with the same embedder used on the chunks.
        // Both sides must use the same embedder, or the numbers are not comparable at all.
        var queryVector = await _embedder.EmbedAsync(query, cancellationToken);

        var vectorHits = _vectors.Search(queryVector, pool);   // by meaning
        var lexicalHits = _lexical.Search(query, pool);        // by words

        return Fuse(vectorHits, lexicalHits, topK);
    }

    /// <summary>
    /// Combine the two result lists. The method is called Reciprocal Rank Fusion (RRF).
    ///
    /// THE PROBLEM IT SOLVES:
    /// BM25 gives scores like 8.4, and cosine gives scores like 0.72. Different scales,
    /// different meanings. You cannot add them, and you cannot compare them.
    ///
    /// THE TRICK:
    /// Throw the scores away. Use only the POSITION in each list.
    ///
    ///     points = 1 / (60 + position)
    ///
    ///     1st place -> 1/61 = 0.0164
    ///     2nd place -> 1/62 = 0.0161
    ///     3rd place -> 1/63 = 0.0159
    ///
    /// Add up the points a chunk earned from both lists.
    ///
    /// Notice the numbers are very close together. That is deliberate. It means a chunk
    /// that is 2nd in BOTH lists (0.0161 + 0.0161 = 0.0322) beats a chunk that is
    /// 1st in one list and absent from the other (0.0164). Agreement wins.
    /// That is exactly what we want from two independent opinions.
    ///
    /// Why 60? It is the default from the research paper. It is NOT a magic number we
    /// tuned. Do not tune it on 8 questions and then trust it.
    /// </summary>
    public static IReadOnlyList<ScoredChunk> Fuse(IReadOnlyList<ScoredChunk> vectorHits, IReadOnlyList<ScoredChunk> lexicalHits, int topK)
    {
        const double rrfK = 60;

        // Keyed by Chunk.Id, so the SAME chunk found by both searches is recognised
        // as one chunk and its points are added together. This is why Chunk needs an Id.
        var scores = new Dictionary<string, double>();
        var chunks = new Dictionary<string, Chunk>();

        Accumulate(vectorHits);
        Accumulate(lexicalHits);

        return scores
            .OrderByDescending(pair => pair.Value)
            .Take(topK)
            .Select(pair => new ScoredChunk(chunks[pair.Key], pair.Value))
            .ToArray();

        // Local function: give every chunk in this list its points.
        void Accumulate(IReadOnlyList<ScoredChunk> hits)
        {
            for (var i = 0; i < hits.Count; i++)
            {
                var id = hits[i].Chunk.Id;

                // i + 1 because i starts at 0 but "first place" should be position 1.
                // GetValueOrDefault: 0 if this chunk was not in the other list.
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (rrfK + i + 1);
                chunks[id] = hits[i].Chunk;
            }
        }
    }
}
