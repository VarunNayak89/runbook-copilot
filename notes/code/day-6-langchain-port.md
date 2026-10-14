# Day 6 — The LangChain port

Concept note: [05-libraries.md](../05-libraries.md)
Code: [`ask.py`](../../projects/langchain-port/ask.py), [`hashing_embeddings.py`](../../projects/langchain-port/hashing_embeddings.py) · [Project README](../../projects/langchain-port/README.md)

---

## Technology used

| Thing | Package | Version (pinned) |
| --- | --- | --- |
| Core abstractions | `langchain-core` | 1.6.7 |
| Splitters | `langchain-text-splitters` | 1.1.3 |
| `EnsembleRetriever` | `langchain-classic` | 1.0.8 |
| `BM25Retriever` | `langchain-community` | 0.4.2 |
| BM25 scoring | `rank-bm25` | 0.2.2 |
| Chat model | `langchain-ollama` | 1.1.0 |
| Umbrella | `langchain` | 1.4.3 |
| Runtime | CPython | 3.12.10 |

Versions are pinned in [`requirements.txt`](../../projects/langchain-port/requirements.txt) with a date, and the reason is in the file.

---

## Class-for-class mapping

| Stage | LangChain (Python) | This project (C#) |
| --- | --- | --- |
| Document loader | `path.read_text()` over `glob` | `KnowledgeBase.LoadAsync` |
| Text splitter | `MarkdownHeaderTextSplitter` + `RecursiveCharacterTextSplitter` | `MarkdownChunker` |
| Embeddings interface | `langchain_core.embeddings.Embeddings` | `IEmbedder` |
| Embeddings impl | `HashingEmbeddings` (ours) | `HashingEmbedder` |
| Vector store | `InMemoryVectorStore` | `VectorIndex` |
| Keyword retriever | `BM25Retriever` | `LexicalIndex` |
| Fusion | `EnsembleRetriever(c=60)` | `HybridSearch.Fuse` |
| Prompt template | `ChatPromptTemplate.from_messages` | `GroundedPrompt.Build` |
| Chat model | `ChatOllama` | `OpenAiChatClient` |
| Eval | `run_eval()` | `RetrievalEval` |
| Document type | `Document(page_content, metadata)` | `Chunk(Id, Source, Heading, Text)` |

Same pipeline. Different names. **That is the entire lesson of Day 6.**

---

## The interface, in both languages

C#:

```csharp
public interface IEmbedder
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<float[]>> EmbedManyAsync(IReadOnlyList<string> texts, CancellationToken ct);
}
```

Python:

```python
class HashingEmbeddings(Embeddings):
    def embed_query(self, text: str) -> list[float]: ...
    def embed_documents(self, texts: list[str]) -> list[list[float]]: ...
```

Identical shape: one for the query, one batched for the corpus. LangChain's `Embeddings` ABC and our `IEmbedder` are the same idea discovered independently — which is a decent signal that the shape is right.

### Reimplementing FNV-1a

```python
def fnv1a(text: str) -> int:
    hash_value = 2166136261
    for ch in text:
        hash_value ^= ord(ch)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF   # <- the detail
    return hash_value
```

`& 0xFFFFFFFF` on every step. C# `uint` wraps at 32 bits automatically; Python integers are arbitrary precision and would grow forever. Forget the mask and the vectors diverge from the C# ones silently — no error, just different search results.

**That is why the port scoring 8/8 means something.** It is evidence the two implementations agree, not a coincidence.

---

## What LangChain actually gave us

```python
heading_splitter = MarkdownHeaderTextSplitter(
    headers_to_split_on=[("#", "heading"), ("##", "heading"), ("###", "heading")],
    strip_headers=False,
)
window_splitter = RecursiveCharacterTextSplitter(chunk_size=1000, chunk_overlap=150)

vector_store = InMemoryVectorStore(embeddings)
vector_store.add_documents(chunks)
vector_retriever = vector_store.as_retriever(search_kwargs={"k": max(top_k, 8)})

lexical_retriever = BM25Retriever.from_documents(chunks, preprocess_func=tokens)

return EnsembleRetriever(retrievers=[vector_retriever, lexical_retriever], weights=[0.5, 0.5], c=60)
```

Nine lines replace roughly 250 lines of C#.

| It wrote for us | We still decided |
| --- | --- |
| the splitters | chunk size 1000, overlap 150 |
| the in-memory store | which embedder |
| the BM25 wrapper | the tokenizer rule |
| RRF arithmetic | c=60, equal weights, fetch 8 return 4 |
| message formatting | what the system prompt says |
| the Ollama client | how we measure success |

**The right column is the hard column, and the library does not help with any of it.**

Note `preprocess_func=tokens` — we hand BM25 our own tokenizer, because the C# `LexicalIndex` splits on alphanumerics only. Without it `"eastus,"` with a comma becomes a different term from `"eastus"` and the two implementations drift.

And `EnsembleRetriever(c=60)` is literally the constant hard-coded in `HybridSearch.Fuse`. Same paper, same default.

---

## Two things that will bite you

### 1. Imports move between versions

The first run failed on this line:

```python
from langchain.retrievers import EnsembleRetriever   # worked in 0.x
```

```
ModuleNotFoundError: No module named 'langchain.retrievers'
```

In 1.4.3 it lives in `langchain_classic.retrievers`. **The pipeline did not change. The package layout did.**

This is why [note 5](../05-libraries.md) says pin your versions, and why `requirements.txt` carries exact numbers and a date.

### 2. `langchain-community` is being sunset

Running the port prints a deprecation warning, because `BM25Retriever` still lives there. The warning is left in deliberately — that is what using this ecosystem is actually like, and you should see it rather than read about it.

---

## The result, and the honest part

Both score `Recall@3: 8/8 = 1.00` on the same 8 questions.

But the rankings are **not identical**:

| Rank | C# | LangChain |
| --- | --- | --- |
| 1 | failover.md — Regional failover | failover.md — Regional failover |
| 2 | failover.md — DNS TTL | failover.md — DNS TTL |
| 3 | **incidents.md — Severity** | **regions.md — Allowed regions** |
| 4 | regions.md — Allowed regions | incidents.md — Severity |

Same four passages, positions 3 and 4 swapped.

**Why:** `MarkdownHeaderTextSplitter` does not cut the files into exactly the chunks `MarkdownChunker` does (16 chunks here, and whitespace handling differs), and `rank_bm25` is a different implementation from our `LexicalIndex`.

This is worth saying out loud in an interview, because blog posts never mention it:

> **Two correct implementations of the same documented design disagree at rank 3.** Recall@3 hides it. A metric that only checked rank 1 would hide it. If answer quality depended on passage 3, the two apps would behave differently and both would look right.

That is also the argument for MRR or nDCG over plain recall.

---

## Running it

```powershell
cd projects\langchain-port
.\.venv\Scripts\python.exe ask.py --eval
.\.venv\Scripts\python.exe ask.py "Which region is paired with eastus for failover?"
.\.venv\Scripts\python.exe ask.py --trace "Which region is paired with eastus for failover?"
```

`--trace` prints the messages LangChain will send. Compare with `trace ask` in C#.

One detail visible there: LangChain names the second role **`human`**. On the wire it is **`user`** — `ChatOllama` renames it when it builds the request. A small reminder that the framework's vocabulary is not the protocol's vocabulary.

---

## Choosing a library, for real

| Need | Use |
| --- | --- |
| .NET, want the standard abstraction | **`Microsoft.Extensions.AI`** — `IChatClient`, `IEmbeddingGenerator` |
| .NET, want plugins and planners | **Semantic Kernel** |
| Python, broad ecosystem, many integrations | **LangChain** |
| Python, index-centric RAG | **LlamaIndex** |
| Stateful multi-step agents | **LangGraph** |
| Tools as a separate service | **MCP** |

A library is a weekend of reading docs once you can draw the pipeline. The pipeline is the career.

---

## What you may now say

You have run a LangChain retrieval pipeline and scored it. Say that.

You have **not** built a LangChain agent, used LangGraph, or deployed any of it. The `agent` command is C#-only. Different claims.

---

## Interview questions on this code

1. What did LangChain write for you, and what did you still decide?
2. Why does the Python FNV-1a need `& 0xFFFFFFFF`?
3. Why pass `preprocess_func` to `BM25Retriever`?
4. Both score 8/8 but rank passages 3 and 4 differently. Why, and why did the metric not notice?
5. Why pin versions in `requirements.txt`?
6. For a .NET team, would you use LangChain? What instead?
