# Day 2 — Retrieval

Concept note: [03-rag.md](../03-rag.md) · Walkthrough with real scores: [question 005](../questions/005-how-retrieval-actually-works.md)
Code: [`Search.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Search.cs), [`Embeddings.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Embeddings.cs), [`KnowledgeBase.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/KnowledgeBase.cs)

---

## Technology used

| Stage | Algorithm, by name | Implemented in |
| --- | --- | --- |
| Tokenizing | alphanumeric split + lowercase | `Tokenizer.Tokens` |
| Keyword ranking | **Okapi BM25** (k₁=1.2, b=0.75) | `LexicalIndex` |
| Embedding | **feature hashing** (the "hashing trick") with **FNV-1a** | `HashingEmbedder` |
| Vector similarity | **cosine similarity** | `VectorMath.Cosine` |
| Vector store | `List<(Chunk, float[])>`, brute-force scan | `VectorIndex` |
| Combining | **Reciprocal Rank Fusion** (k=60) | `HybridSearch.Fuse` |

Four classic information-retrieval algorithms. Three of them predate deep learning. **Retrieval is search engineering, not AI.**

---

## BM25 — `LexicalIndex`

### The state it keeps

```csharp
private readonly List<Chunk> _chunks = new();
private readonly List<Dictionary<string, int>> _termFrequency = new();   // per chunk: word -> count
private readonly List<int> _lengths = new();                             // per chunk: total words
private readonly Dictionary<string, int> _documentFrequency = new();     // word -> how many chunks contain it
```

This is a **forward index** (chunk → terms). A production engine uses an **inverted index** (term → chunks), which is the single most important difference and the reason ours is O(n) per query.

### Build — `Add`, O(words in chunk)

```csharp
foreach (var token in Tokenizer.Tokens(chunk.Heading + " " + chunk.Text))
    counts[token] = counts.GetValueOrDefault(token) + 1;

foreach (var term in counts.Keys)                                    // Keys, not tokens
    _documentFrequency[term] = _documentFrequency.GetValueOrDefault(term) + 1;
```

Two details that matter:

- **`chunk.Heading + " " + chunk.Text`** — the heading is indexed. "DNS TTL" may exist only as a heading; without this it is unfindable.
- **`counts.Keys`** — each word counts **once per chunk** for document frequency. That is the definition of df: *in how many documents*, not *how many times*.

### Query — `Search`, O(chunks × query terms)

```csharp
var df  = _documentFrequency[term];
var idf = Math.Log(1 + (_chunks.Count - df + 0.5) / (df + 0.5));
var denominator = tf + K1 * (1 - B + B * _lengths[i] / averageLength);
score += idf * (tf * (K1 + 1)) / denominator;
```

Three components, each doing a job:

| Part | Formula | Effect |
| --- | --- | --- |
| **idf** | `log(1 + (N − df + 0.5)/(df + 0.5))` | rare word → high score; "the" → ≈0 |
| **tf saturation** | `tf(k₁+1) / (tf + …)` | the 10th occurrence adds far less than the 2nd |
| **length norm** | `b × len/avgLen` | long chunks are penalised |

`k₁ = 1.2` controls how fast term frequency saturates. `b = 0.75` controls how hard length is penalised (0 = ignore length, 1 = full). These are the standard defaults from the BM25 paper — **do not tune them to fix a bad result.** Fix the chunks instead.

The `+0.5` terms are smoothing, so df=0 or df=N never divides by zero.

### Measured

For *"Which region is paired with eastus for failover?"*:

```
15.7089  failover.md  Regional failover     <- 'failover', 'paired', 'eastus' are rare here
 4.3583  failover.md  DNS TTL
 3.0119  regions.md   Allowed regions
```

A 3.6× gap to second place. When the question shares rare words with a chunk, BM25 is decisive.

---

## Feature hashing — `HashingEmbedder`

### The whole algorithm

```csharp
var vector = new float[384];
foreach (var token in Tokenizer.Tokens(text))
{
    var hash   = Fnv1a(token);                            // word -> uint32
    var bucket = (int)(hash % 384);                       // which slot
    var sign   = (hash & 0x8000_0000) == 0 ? 1f : -1f;    // +1 or -1
    vector[bucket] += sign;
}
```

This is the **hashing trick** (Weinberger et al., 2009). It predates word embeddings entirely.

**Why the random sign?** 384 slots must hold thousands of distinct words, so collisions are certain. With a random ±1, colliding words tend to cancel rather than always reinforce — which keeps collisions from manufacturing false similarity. This is the signed-hashing variant, and the sign is not decoration.

### Why FNV-1a and not `GetHashCode()`

```csharp
const uint offset = 2166136261;   // FNV offset basis
const uint prime  = 16777619;     // FNV prime
hash ^= ch;
hash *= prime;
```

.NET randomises `string.GetHashCode()` **per process** (a defence against hash-flooding DoS). Use it here and your vectors change after every restart, so any persisted index silently stops matching.

FNV-1a is deterministic forever, on any machine, in any language. That is exactly why the Python port can reimplement it in 6 lines and get identical vectors.

### Cosine similarity

```csharp
return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
```

Dividing by both magnitudes removes **length** and leaves **direction**. Length says "how much text"; direction says "about what". A long page and a short paragraph on the same topic should match.

Zero-vector guard returns 0 rather than `NaN`.

### Honest assessment

| | This | A real embedding model |
| --- | --- | --- |
| Trained on language? | **no** | yes |
| "paired region" ≈ "failover partner"? | **no** | yes |
| Dimensions | 384 | 384–3072 |
| Cost | free | per token |
| Deterministic | yes | yes |
| Needs network | no | yes |

It behaves closer to **weighted keyword overlap** than to semantic search. Say "hashing embedder" in an interview, never just "embeddings".

**Swap it:** set `COPILOT_EMBEDDING_MODEL`. `IEmbedder` is the seam; nothing else changes.

---

## `VectorIndex` — the "vector database"

```csharp
private readonly List<(Chunk Chunk, float[] Vector)> _items = new();

public IReadOnlyList<ScoredChunk> Search(float[] query, int topK) =>
    _items.Select(item => new ScoredChunk(item.Chunk, VectorMath.Cosine(query, item.Vector)))
          .OrderByDescending(item => item.Score)
          .Take(topK)
          .ToArray();
```

A **brute-force k-NN scan**. Compare the query to every vector.

| | Complexity | At 16 chunks | At 10M chunks |
| --- | --- | --- | --- |
| Brute force (ours) | O(n·d) | ~6k ops, instant | ~4 billion ops per query |
| HNSW (real stores) | ~O(log n · d) | pointless | milliseconds |

**HNSW** (Hierarchical Navigable Small World) is the index real vector databases use — a navigable graph that is *approximate*, trading a little recall for enormous speed. That trade-off is the thing to know: a production vector search may legitimately miss a result.

Azure AI Search, pgvector, Qdrant, Pinecone, Milvus are this class plus HNSW, persistence, filters and sharding.

---

## RRF — `HybridSearch.Fuse`

### The problem, in numbers

BM25 returns **15.7089**. Cosine returns **0.7770**. Unbounded vs [−1,1]. Adding them is meaningless and BM25 would dominate every time. Normalising (min-max, z-score) is possible but fragile — one outlier moves every score.

### The solution: discard the scores, keep the ranks

```csharp
const double rrfK = 60;
scores[id] = scores.GetValueOrDefault(id) + 1.0 / (rrfK + i + 1);
```

```
rank 1 -> 1/61 = 0.016393
rank 2 -> 1/62 = 0.016129
rank 3 -> 1/63 = 0.015873
```

Scale-free, outlier-proof, and extends to any number of rankers.

### Why the values are so close

| Chunk | BM25 rank | Vector rank | Sum |
| --- | --- | --- | --- |
| failover.md / Regional failover | 1 → .016393 | 1 → .016393 | **.032787** |
| failover.md / DNS TTL | 2 → .016129 | 3 → .015873 | **.032002** |
| incidents.md / Severity | 5 → .015385 | 2 → .016129 | **.031513** |

Two second places (.03226) beat one first place alone (.016393). **Agreement between independent rankers wins** — that is the entire design intent.

`k=60` is the constant from Cormack et al. (2009). A larger k flattens the ranks further; a smaller k makes rank 1 dominate. Do not tune it on 8 questions.

**Keyed by `Chunk.Id`** — the same chunk found by both rankers must merge, which is why `Chunk` carries `"failover.md#0"`.

### The over-fetch

```csharp
var pool = Math.Max(topK, 8);          // ask each ranker for 8
return Fuse(vectorHits, lexicalHits, topK);   // return 4
```

A chunk at rank 6 in both lists can beat a chunk at rank 1 in one. Fetch deeper than you return.

---

## Diagnosing the distractor

Day 1 showed `incidents.md` as passage 3, answering nothing. The two rankers disagree about it:

| | rank |
| --- | --- |
| BM25 | 5th — correctly unimpressed |
| Vector | **2nd** |

**The hashing embedder caused it.** `incidents.md` shares common words and mentions `billing-api`; a bag-of-words vector cannot tell "severity" from "regions".

So the fix is **not** the prompt and **not** the RRF constant. It is a better embedder, or a reranker. Being able to say which component is at fault, with numbers, is the difference between engineering and guessing.

---

## What you would use in production

| This project | Production | Why |
| --- | --- | --- |
| forward index, O(n) scan | inverted index (Lucene/Elastic) | O(matching docs) |
| brute-force k-NN | HNSW in a vector DB | sub-linear |
| hashing embedder | `text-embedding-3-small`, `nomic-embed-text` | actual semantics |
| re-embed at startup | embed on write, persist vectors | embedding costs money |
| no reranker | cross-encoder (Cohere Rerank, `bge-reranker`) | **the fix for our distractor** |
| no filters | metadata filter **before** search | multi-tenant isolation |
| in-process | Azure AI Search / pgvector / Qdrant | survives restart, scales |

**Two stages we are missing entirely**, and you should name both:

1. **Reranking** — a second, slower, more accurate pass over the top ~20. Directly fixes passage 3.
2. **Metadata filtering** — restrict to this tenant *before* retrieval. Not a quality issue, a **data-leak** issue.

---

## Interview questions on this code

1. BM25 gave 15.7, cosine gave 0.78. Why can you not add them?
2. What does `b = 0.75` control, and when would you lower it?
3. Why FNV-1a instead of `string.GetHashCode()`?
4. Why does feature hashing use a random sign?
5. Why index the heading along with the body?
6. Why fetch 8 and return 4?
7. Our vector search is O(n). What changes at 10 million chunks, and what do you give up?
8. Which component put `incidents.md` in the results, and what is your fix?

---

## Try it

```powershell
dotnet run --project src/RunbookCopilot -- eval
dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
```

Then open `Search.cs` and change `rrfK` from 60 to 1. Re-run `eval`. Put it back.
