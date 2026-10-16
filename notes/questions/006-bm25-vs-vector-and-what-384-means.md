# 006 — Does BM25 use embeddings? And what does 384 mean?

**Asked on 2026-10-15, while reading [notes/03-rag.md](../03-rag.md).**

> Do we break the question into chunks too? Does BM25 also do embedding? Why 384 — is that fixed? Does the chunk get the same 384? Do real projects use both searches?

---

## Short answer — your own summary, and it is correct

> - **BM25** does no embedding at all, but it **breaks the question into words**.
> - **Vector search** uses the **entire question**, no breaking, but it **does embedding**.
> - We use **384 dimensions**. Real projects often use more.
> - That number **must be the same** for the question and for the document chunks.

All four are right. The rest of this page is the evidence.

---

# Part 1 — The question is never chunked

| | Broken into pieces? | Why |
| --- | --- | --- |
| **Documents** | Yes → 16 chunks | They are long. Too big to send, too big to search well. |
| **Question** | **No** | It is already short. It is one idea. |

Splitting *"Which region is paired with eastus for failover?"* would destroy the link between "eastus" and "paired".

### A vocabulary warning

Do **not** say "BM25 chunks the question". In this project **chunk** means *a piece of a document*. Using it for words will confuse you later.

The correct word is **tokenize**. The pieces are **tokens**, or just **words**.

| Operation | Input | Output | Word for it |
| --- | --- | --- | --- |
| Cutting a document | `failover.md` | 3 chunks | **chunking** |
| Cutting a question | the sentence | 8 words | **tokenizing** |

---

# Part 2 — BM25 does NO embedding

This is the correction that matters most.

## What BM25 actually stores

For the chunk `failover.md / Regional failover`:

```
"failover"    -> appears 5 time(s)
"paired"      -> appears 3 time(s)
"for"         -> appears 2 time(s)
"is"          -> appears 2 time(s)
"region"      -> appears 2 time(s)
"regional"    -> appears 2 time(s)
"with"        -> appears 2 time(s)
"eastus"      -> appears 1 time(s)
"westus"      -> appears 1 time(s)
"northeurope" -> appears 1 time(s)
...18 entries total
```

**That is a `Dictionary<string, int>`.** Word → count. There is no vector anywhere.

## How it scores a word

One dictionary lookup:

```
To score 'eastus':  does the dictionary contain the key "eastus"?  -> yes, 1 time
To score 'paired':  -> yes, 3 times
```

Like `myDict.ContainsKey("eastus")`. It compares **literal strings**.

## So where does 15.7089 come from?

From **arithmetic on counts** — how often the word appears, how rare it is across all chunks, how long the chunk is.

> The number is a **score**. It is not a representation of the text.

| | BM25 | Embedding |
| --- | --- | --- |
| Numbers made from | **counting words** | **the text itself** |
| The number is | a score | a representation |
| Can you recover the words? | yes, stored as strings | **no, they are gone** |

## The proof that BM25 is blind to meaning

```
To score 'partner' (same meaning as 'paired'):
   -> NO. BM25 cannot see that 'partner' means 'paired'.
```

BM25 only matches letters. **That is exactly why the second search exists.**

## The loop

```
for each of the 16 chunks:
      for each of the 8 words in the question:
            does this chunk contain that word?  add points
```

16 × 8 = **128 checks**.

### Why common words do not ruin it

Your question tokenizes to: `which` `region` `is` `paired` `with` `eastus` `for` `failover`

| word | in N of 16 chunks | idf | worth |
| --- | --- | --- | --- |
| which | 0 | 0.00 | nothing — not in the corpus |
| is | 8 | 0.69 | almost nothing |
| region | 5 | 1.13 | medium |
| eastus | 3 | 1.58 | a lot |
| failover | 3 | 1.58 | a lot |
| **paired** | **1** | **2.43** | **most of the score** |

`idf` means *inverse document frequency*: rare words are worth more. You never write a stop-word list — the maths handles it.

> **An honest caveat.** In that table `with` scored 1.92 and looks "rare". It is not; our corpus is only 16 chunks, so a common word can look rare by accident. **IDF needs a reasonably large corpus to behave.** This is one reason small demos produce odd rankings.

---

# Part 3 — Vector search embeds the whole question

No splitting at all:

```
questionVector = embed("Which region is paired with eastus for failover?")   // ONE vector

for each of the 16 chunks:
      score = cosine(questionVector, chunkVector)
```

**16 comparisons.** That is the whole loop.

Here is the same chunk as a vector:

```
[0, 0, 0, 0, 0, 0, 0, 0, -3, 0, 0, 0, ...]   384 numbers
```

**You cannot read a word out of it.** The words are gone. That is the trade: you lose the exact words, you gain the ability to match meaning.

---

# Part 4 — What 384 means

It is the **number of slots** in the list. The proper word is **dimensions**.

```
[0, 0, -3, 0, 0, 1, 0, ... ]
 ^  ^   ^
 1  2   3   ... up to 384
```

- In **our fake embedder**: each word hashes into one of 384 buckets. 384 = how many buckets.
- In a **real embedder**: 384 is the output size of the neural network.

## Is it fixed?

**In our code, no** — it is a constructor parameter:

```
HashingEmbedder(  64)  ->    64 numbers,   5 non-zero
HashingEmbedder( 384)  ->   384 numbers,   7 non-zero
HashingEmbedder(1536)  ->  1536 numbers,   7 non-zero
```

Look carefully: the same text gives **5** non-zero slots at 64 dimensions but **7** at 384. Two words collided into one bucket. **Fewer slots means more collisions means information lost.**

**In a real model, yes — completely fixed.** The architecture decides it, and you cannot change it.

| Real model | Dimensions |
| --- | --- |
| `all-MiniLM-L6-v2` | **384** |
| `nomic-embed-text` | 768 |
| `text-embedding-3-small` | 1536 |
| `text-embedding-3-large` | 3072 |

384 is a real and common size — it is MiniLM's output size, which is almost certainly why this project picked it.

## More dimensions: better or worse?

A trade-off, not an upgrade.

| | Few (384) | Many (3072) |
| --- | --- | --- |
| Detail captured | less | more |
| Memory | small | large |
| Search speed | fast | slow |

For **1 million chunks**, at 4 bytes per number:

```
 384 dims  ->   1.43 GB
 768 dims  ->   2.86 GB
1536 dims  ->   5.72 GB
3072 dims  ->  11.44 GB
```

8× the dimensions is 8× the RAM. Many teams find 768 is enough and keep the memory.

---

# Part 5 — The question and the chunks must match

```
question -> 384 numbers
chunk    -> 384 numbers
cosine   -> 0.8018   (works, because both are 384)
```

If they do not match it is not merely wrong, it **throws**:

```
Can you compare a 384-vector with a 1536-vector?
   NO -> Vectors must have the same number of dimensions.
```

That check is real, in `VectorMath.Cosine`:

```csharp
if (left.Length != right.Length)
    throw new ArgumentException("Vectors must have the same number of dimensions.");
```

## Why this matters in production

> **The question and the documents must go through the same embedder.**

Change your embedding model and you must **re-embed your entire corpus**. There is no shortcut, no conversion, no partial migration.

For a million documents that is real time and real money. It is one of the reasons teams think hard before picking an embedding model, and why they version their index alongside the model name.

---

# Part 6 — Do real projects use both?

**Yes. Hybrid search is standard in serious RAG systems.**

| Product | Hybrid built in? |
| --- | --- |
| Azure AI Search | yes — keyword + vector + RRF + a semantic reranker |
| Elasticsearch / OpenSearch | yes |
| Qdrant, Weaviate, Vespa | yes |

Being accurate about the landscape:

| Setup | Who uses it |
| --- | --- |
| Vector only | simple projects, demos, most tutorials |
| **Hybrid (both)** | **serious production systems** |
| Hybrid + **reranker** | the quality-focused ones |

**Our project uses the good pattern.** What it lacks is the third stage — reranking — which is precisely the fix for the `incidents.md` distractor.

---

# The whole thing in one table

| | BM25 | Vector search |
| --- | --- | --- |
| Question is | split into 8 **words** | kept **whole** |
| Question becomes | 8 strings | 1 vector of 384 numbers |
| Chunk is stored as | `Dictionary<string,int>` | `float[384]` |
| Comparison | does the key exist? | cosine of two vectors |
| **Uses embedding?** | **NO** | **YES** |
| Sees "partner" = "paired"? | no | yes |
| Good at exact codes | **yes** | no |
| Good at paraphrase | no | **yes** |

**Only one of the two searches uses embeddings.** That is the sentence to remember.

---

## Check yourself

1. BM25 stores what, exactly, for each chunk? Name the C# type.
2. Where does the number 15.7089 come from, if there is no embedding?
3. Why can BM25 not match "failover partner" against "paired region"?
4. What does 384 mean, and who decides it for a real model?
5. You switch from `all-MiniLM-L6-v2` to `text-embedding-3-small`. What work does that force on you?
6. Does a serious production system use keyword search, vector search, or both?

---

## Related

- [notes/03-rag.md](../03-rag.md) — the RAG note in plain English
- [notes/code/day-2-retrieval.md](../code/day-2-retrieval.md) — the BM25 formula, HNSW, complexity
- [question 005](005-how-retrieval-actually-works.md) — the full retrieval walkthrough with scores
