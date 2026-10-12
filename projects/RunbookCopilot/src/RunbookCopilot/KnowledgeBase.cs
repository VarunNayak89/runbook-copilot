// ============================================================================
// KnowledgeBase.cs - the setup step that happens before any question is asked.
//
// This is the "ingestion pipeline", and it is four lines of real work:
//
//     1. READ   every markdown file          (LangChain calls this a document loader)
//     2. CHUNK  each one into pieces          (a text splitter)
//     3. EMBED  every chunk into numbers      (an embedding model)
//     4. INDEX  them for both kinds of search (a vector store)
//
// Do this once at startup. Then every question is fast, because the hard work is done.
//
// A REAL LIMITATION TO KNOW: this app redoes all of it every time the process starts.
// That is fine for 7 small files. It is wrong for a large, changing document set,
// where you would embed a document once when it changes and store the vectors in a
// database. Say this limitation out loud if someone asks. It shows you know the
// difference between a teaching project and a product.
// ============================================================================

namespace RunbookCopilot;

/// <summary>Holds the finished chunks and the search built over them.</summary>
public sealed class KnowledgeBase
{
    public KnowledgeBase(IReadOnlyList<Chunk> chunks, HybridSearch search)
    {
        Chunks = chunks;
        Search = search;
    }

    public IReadOnlyList<Chunk> Chunks { get; }

    public HybridSearch Search { get; }

    public static async Task<KnowledgeBase> LoadAsync(string contentRoot, IEmbedder embedder, CancellationToken cancellationToken)
    {
        // --- 1 and 2: read every file, and chunk it ---
        var chunker = new MarkdownChunker();
        var chunks = new List<Chunk>();
        var runbookDirectory = Path.Combine(contentRoot, "runbooks");

        // OrderBy so the chunk ids come out the same on every run and on every machine.
        // Directory.GetFiles does not promise an order. Without this, two runs could
        // number the chunks differently and results would wobble for no reason.
        foreach (var file in Directory.GetFiles(runbookDirectory, "*.md").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var markdown = await File.ReadAllTextAsync(file, cancellationToken);

            // We pass only the FILE NAME, not the full path. That name is what the
            // answer will cite, and a full path would leak your folder layout into
            // the prompt and into every answer.
            chunks.AddRange(chunker.Chunk(Path.GetFileName(file), markdown));
        }

        // Fail loudly and early. Without this, search would silently return nothing and
        // the model would answer from memory - which is the exact failure RAG exists
        // to prevent. A clear error now is much better than wrong answers later.
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException($"No markdown runbooks were found in {runbookDirectory}.");
        }

        // --- 3: embed every chunk ---
        // All at once, not one at a time. With a remote embedder that is the difference
        // between one batched call and dozens of separate network round trips.
        var vectors = await embedder.EmbedManyAsync(chunks.Select(chunk => chunk.Text).ToArray(), cancellationToken);

        // --- 4: build BOTH indexes from the same chunks ---
        // The vector index for meaning, the lexical index for words. Same input,
        // two different ways to find it later. See Search.cs.
        var vectorIndex = new VectorIndex();
        var lexicalIndex = new LexicalIndex();
        for (var i = 0; i < chunks.Count; i++)
        {
            vectorIndex.Add(chunks[i], vectors[i]);
            lexicalIndex.Add(chunks[i]);
        }

        // The SAME embedder instance goes into HybridSearch. This is important:
        // the question must be embedded by the same model that embedded the chunks,
        // or the vectors live in different spaces and the comparison is meaningless.
        return new KnowledgeBase(chunks, new HybridSearch(embedder, vectorIndex, lexicalIndex));
    }
}

public static class ContentPaths
{
    public static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var runbooks = Path.Combine(directory.FullName, "content", "runbooks");
            if (Directory.Exists(runbooks))
            {
                return Path.Combine(directory.FullName, "content");
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find content/runbooks. Build the project so the runbooks are copied next to the app.");
    }
}
