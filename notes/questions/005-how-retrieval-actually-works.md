# 005 — How does `trace ask` actually find the right passage?

**Asked on 2026-10-13.**

> How does it find the passage that has that word? Did you use a vector database or embeddings here? Show me the code where this logic is implemented.

---

## Short answer

| Question | Answer |
| --- | --- |
| Embeddings? | **Yes** — but a fake, offline one, not a neural model |
| Vector database? | **No** — a `List<>` in memory |
| Keyword search too? | **Yes** — BM25, and it does most of the work here |
| How are the two combined? | Reciprocal Rank Fusion |

Four files, about 250 lines total. No library, no database, no network.

---

## One correction first

> "here we don't use LLM in this repo"

Not quite. The LLM **is** wired up and working — you ran it on Day 4 with Ollama and `llama3.2`.

What is true is that these commands never call it:

| Command | Calls the LLM? |
| --- | --- |
| `trace ask` / `trace agent` | **Never.** It prints the request instead of sending it. |
| `eval` | **Never.** It scores search only. |
| `ask` / `agent` / `extract` | **Yes**, when `COPILOT_CHAT_MODEL` is set |

So **retrieval has nothing to do with the LLM.** Search runs entirely in your C#, before any model is involved. That separation is the point: you can measure and fix retrieval without ever paying for a model call.

---

## Watch it happen, with real numbers

For the question *"Which region is paired with eastus for failover?"*, here is what each stage actually produced:

```
STEP 1  chunked 7 files into 16 chunks

STEP 2  embedder = hashing-384  (each chunk is now 384 numbers)

STEP 3a  BM25 keyword search says:
   1.  15.7089  failover.md      Regional failover
   2.   4.3583  failover.md      DNS TTL
   3.   3.0119  regions.md       Allowed regions
   4.   2.6463  failover.md      Provision lock
   5.   2.5996  incidents.md     Severity

STEP 3b  Vector meaning search says:
   1.   0.7770  failover.md      Regional failover
   2.   0.3046  incidents.md     Severity
   3.   0.2182  failover.md      DNS TTL
   4.   0.1581  trials.md        Trial storage
   5.   0.1543  regions.md       Allowed regions

STEP 4   Fused with RRF  (this is what ask sends):
   1.   0.0328  failover.md      Regional failover
   2.   0.0320  failover.md      DNS TTL
   3.   0.0315  incidents.md     Severity
   4.   0.0313  regions.md       Allowed regions
```

Three things to notice before reading on:

1. **Both methods put the right chunk first.** Good.
2. **The score scales are completely different** — 15.7089 versus 0.7770. You cannot add those numbers together. That problem is why step 4 exists.
3. **Look where `incidents.md` comes from.** BM25 ranks it 5th. The vector search ranks it **2nd**. That is where your distractor comes from — and now you know exactly which half of the system caused it.

---

## Step 1 — Chunking

**File: [`Chunking.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Chunking.cs) → `MarkdownChunker.Chunk`**

7 markdown files become 16 chunks. It splits on every `#`, `##` or `###` heading, then windows anything still over 1000 characters.

```csharp
foreach (var line in lines)
{
    if (IsHeading(line))
    {
        Emit();                                 // finish the previous section
        heading = line.TrimStart('#').Trim();   // start a new one
    }
    else
    {
        body.AppendLine(line);
    }
}
```

One detail that matters: the heading is put back **inside** the chunk text.

```csharp
var combined = $"# {title}\n{text}";
```

Without that, a search for "DNS TTL" could not find the section called *DNS TTL*, because those letters appear only in the heading and not in the sentences below it.

---

## Step 2 — Embeddings (the fake kind)

**File: [`Embeddings.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Embeddings.cs) → `HashingEmbedder`**

Each chunk becomes 384 numbers. Here is the entire algorithm:

```csharp
var vector = new float[384];
foreach (var token in Tokenizer.Tokens(text))
{
    var hash = Fnv1a(token);                               // word -> a number
    var bucket = (int)(hash % 384);                        // which of the 384 slots
    var sign = (hash & 0x8000_0000) == 0 ? 1f : -1f;       // +1 or -1
    vector[bucket] += sign;
}
```

That is it. Hash each word, pick a slot, add or subtract 1.

**Be honest about what this is.** A real embedding model was *trained on language*, so it knows "paired region" and "failover partner" mean the same thing. This one does not. It just counts words in a clever way. It is closer to keyword matching than to understanding.

It is here so the project runs offline, instantly, free, and gives the same answer every time. Set `COPILOT_EMBEDDING_MODEL` to swap in a real one — `IEmbedder` is the seam, and nothing else changes.

Comparison is cosine similarity — *do these two vectors point the same way?*

```csharp
return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
```

Direction, not distance. Length means "how much text". Direction means "about what".

---

## Step 3a — BM25, the keyword search

**File: [`Search.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Search.cs) → `LexicalIndex`**

BM25 is the standard keyword ranking formula. Elasticsearch and Lucene use it. It is 30 years old and has nothing to do with AI.

It scores a chunk higher when:

1. the word appears **often** in that chunk
2. the word is **rare** across all chunks ← the clever part
3. the chunk is **short**

```csharp
var df = _documentFrequency[term];                                   // in how many chunks?
var idf = Math.Log(1 + (_chunks.Count - df + 0.5) / (df + 0.5));     // rare = valuable
var denominator = tf + K1 * (1 - B + B * _lengths[i] / averageLength);
score += idf * (tf * (K1 + 1)) / denominator;
```

Point 2 is why BM25 scored `Regional failover` at **15.7089** while everything else was under 4.4. The words *failover*, *paired* and *eastus* are rare in this corpus, so matching them is worth a lot. The word "the" would be worth almost nothing.

---

## Step 3b — The vector search

**File: [`Search.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Search.cs) → `VectorIndex`**

Here is your "vector database", in full:

```csharp
public sealed class VectorIndex
{
    private readonly List<(Chunk Chunk, float[] Vector)> _items = new();

    public void Add(Chunk chunk, float[] vector) => _items.Add((chunk, vector));

    public IReadOnlyList<ScoredChunk> Search(float[] query, int topK)
    {
        return _items
            .Select(item => new ScoredChunk(item.Chunk, VectorMath.Cosine(query, item.Vector)))
            .OrderByDescending(item => item.Score)
            .Take(topK)
            .ToArray();
    }
}
```

A `List`, and a loop that compares the question against **every** chunk.

For 16 chunks that is instant. For 10 million it would be far too slow, and you would use Azure AI Search, pgvector or Qdrant instead. **Those products are this same idea plus speed tricks, filters and persistence.** The idea is what you are learning. The product name is shopping.

---

## Step 4 — Fusing the two lists

**File: [`Search.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Search.cs) → `HybridSearch.Fuse`**

### The problem

BM25 says **15.7089**. Cosine says **0.7770**. Different scales, different meanings. Adding them is meaningless, and whichever has bigger numbers would always win.

### The trick: throw the scores away

Use only the **position** in each list:

```csharp
scores[id] = scores.GetValueOrDefault(id) + 1.0 / (rrfK + i + 1);   // rrfK = 60
```

```
1st place -> 1/61 = 0.01639
2nd place -> 1/62 = 0.01613
3rd place -> 1/63 = 0.01587
```

Add up the points a chunk earned from both lists.

### Why the numbers are so close together

That is deliberate. Watch what it buys you:

| Chunk | BM25 rank | Vector rank | Total |
| --- | --- | --- | --- |
| failover.md / Regional failover | 1st → 0.01639 | 1st → 0.01639 | **0.0328** |
| failover.md / DNS TTL | 2nd → 0.01613 | 3rd → 0.01587 | **0.0320** |
| incidents.md / Severity | 5th → 0.01538 | 2nd → 0.01613 | **0.0315** |

Those add up to exactly the step 4 output above.

Because the gaps are small, **a chunk that both methods like beats a chunk that only one method loves.** Agreement wins. That is precisely what you want from two independent opinions.

**Why 60?** It is the default from the research paper. It is not a number we tuned, and you should not tune it on eight questions and then trust it.

---

## Where the distractor really comes from

You noticed on Day 1 that `incidents.md` is passage 3 and answers nothing. Now you can say exactly why:

- BM25 ranked it **5th** — correctly unimpressed
- The hashing embedder ranked it **2nd**
- Fusion split the difference and landed it at 3rd

**The weak fake embedder caused it.** `incidents.md` mentions `billing-api` and `Sev1`, shares a few common words, and a bag-of-words vector cannot tell that it is about severity rather than regions.

So the fix is not the prompt, and not the fusion constant. It is a better embedder, or a reranking step. That is a real, specific diagnosis — exactly the kind of thing to say in an interview.

---

## Putting it together

**File: [`KnowledgeBase.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/KnowledgeBase.cs)** does steps 1–3 once at startup:

```csharp
var vectors = await embedder.EmbedManyAsync(chunks.Select(chunk => chunk.Text).ToArray(), cancellationToken);
var vectorIndex = new VectorIndex();
var lexicalIndex = new LexicalIndex();
for (var i = 0; i < chunks.Count; i++)
{
    vectorIndex.Add(chunks[i], vectors[i]);
    lexicalIndex.Add(chunks[i]);
}
```

Both indexes, from the same chunks. Then every question is just:

```csharp
var queryVector = await _embedder.EmbedAsync(query, cancellationToken);
var vectorHits = _vectors.Search(queryVector, pool);
var lexicalHits = _lexical.Search(query, pool);
return Fuse(vectorHits, lexicalHits, topK);
```

Note it asks each search for **8** results but returns only 4. A chunk ranked 6th and 7th can still win after fusion — if you only looked at each list's top 4 you would never see it.

---

## The whole pipeline, in one place

| Step | File | Class | What it does |
| --- | --- | --- | --- |
| 1 | `Chunking.cs` | `MarkdownChunker` | 7 files → 16 chunks |
| 2 | `Embeddings.cs` | `HashingEmbedder` | each chunk → 384 numbers |
| 3a | `Search.cs` | `LexicalIndex` | BM25 keyword ranking |
| 3b | `Search.cs` | `VectorIndex` | cosine similarity ranking |
| 4 | `Search.cs` | `HybridSearch.Fuse` | combine both by rank |
| — | `KnowledgeBase.cs` | `KnowledgeBase` | runs 1–3 at startup |
| — | `GroundedPrompt.cs` | `GroundedPrompt` | pastes the winners into the prompt |

**Only the last row involves the model.** Everything above it is ordinary C# you could have written before you had ever heard of AI.

That is the thing most people get wrong about RAG. It is mostly search engineering.

---

## Check yourself

1. BM25 gave 15.7089 and cosine gave 0.7770. Why can you not just add them?
2. Which of the two searches is responsible for `incidents.md` appearing? What would you change first?
3. Why does `SearchAsync` ask for 8 results when it only returns 4?
4. Why is the heading copied into the chunk's text?

---

## Related

- [notes/03-rag.md](../03-rag.md) — the RAG note
- [notes/reference/production-genai-systems.md](../reference/production-genai-systems.md) — reranking and metadata filtering, the two stages we do not have
- Labs 2 and 3 in [LABS.md](../../projects/RunbookCopilot/LABS.md) — change the corpus and the chunk size, and watch these numbers move
