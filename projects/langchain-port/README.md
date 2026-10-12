# LangChain port of `ask`

The same retrieval pipeline as [RunbookCopilot](../RunbookCopilot/README.md), written with LangChain instead of by hand. Same corpus, same embedder, same fusion constant, same grounded prompt, same eval questions.

The point is not that Python is better. The point is that after you run both, "I have used LangChain" is a true sentence, and you can say exactly what the library did and did not decide for you.

## Run

The virtual environment is already created and the packages are installed.

```powershell
cd projects\langchain-port
.\.venv\Scripts\python.exe ask.py --eval
.\.venv\Scripts\python.exe ask.py "Which region is paired with eastus for failover?"
.\.venv\Scripts\python.exe ask.py --trace "Which region is paired with eastus for failover?"
```

To rebuild it from nothing on another machine:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

## What maps to what

Every line of `ask.py` has a counterpart in the C#. That is the table from [note 5](../../notes/05-libraries.md), now with code on both sides.

| LangChain | In `ask.py` | In the C# app |
| --- | --- | --- |
| Document loader | `path.read_text` over `RUNBOOKS.glob` | `KnowledgeBase.LoadAsync` |
| Text splitter | `MarkdownHeaderTextSplitter` + `RecursiveCharacterTextSplitter` | `MarkdownChunker` |
| Embeddings | `HashingEmbeddings` (`hashing_embeddings.py`) | `HashingEmbedder` |
| Vector store | `InMemoryVectorStore` | `VectorIndex` |
| Keyword retriever | `BM25Retriever` | `LexicalIndex` |
| Fusion | `EnsembleRetriever(c=60)` | `HybridSearch.Fuse` |
| Prompt template | `ChatPromptTemplate.from_messages` | `GroundedPrompt.Build` |
| Chat model | `ChatOllama` | `OpenAiChatClient` |
| Eval | `run_eval` | `RetrievalEval` |

`hashing_embeddings.py` is the one thing LangChain does not give you, and it is deliberate. Implementing `Embeddings` with the exact FNV-1a algorithm from the C# means both projects search with the same signal, so a difference in the results is a difference in the pipeline, not in the embedder. It also keeps the port runnable with no model download. It is a weak embedder; swap it for `OllamaEmbeddings(model="nomic-embed-text")` and nothing else in the file changes. That is what an interface is for, and it is the same `IEmbedder` seam in C#.

## The result, and the honest part

Both projects score the same on the same eight questions:

```
Recall@3: 8/8 = 1.00
```

The rankings are not identical, though. On "Which region is paired with eastus for failover?":

| Rank | C# | LangChain |
| --- | --- | --- |
| 1 | failover.md — Regional failover | failover.md — Regional failover |
| 2 | failover.md — DNS TTL | failover.md — DNS TTL |
| 3 | incidents.md — Severity | regions.md — Allowed regions |
| 4 | regions.md — Allowed regions | incidents.md — Severity |

Same four passages, positions 3 and 4 swapped. Same constants, same corpus, same fusion arithmetic, different result at the tail — because `MarkdownHeaderTextSplitter` does not cut the files into exactly the chunks `MarkdownChunker` does, and `rank_bm25` is not the same implementation as `LexicalIndex`.

This is worth saying out loud in an interview, because it is the thing blog posts skip: **two correct implementations of the same documented design disagree on rank 3.** Recall@3 hides it. A metric that only looked at rank 1 would hide it. If the answer quality depended on passage 3, the two apps would behave differently and both would look right.

## What LangChain actually did for you

It wrote the plumbing: the splitters, the BM25 wrapper, the in-memory store, the RRF arithmetic, the message formatting, the Ollama client. In C# that was `Chunking.cs`, `Search.cs`, `Embeddings.cs`, and `Chat.cs`. Real work, and worth not writing twice.

It decided none of the things that are hard:

- what the chunks are, and how big
- which passages reach the prompt
- what the system message says
- how you know retrieval is working
- what a tool is allowed to do

Those are the same decisions in both files. That is why [note 8](../../notes/08-the-request-body.md) is the note that matters more than this folder: whatever LangChain assembles, it ends up posting a `messages` array to a URL.

## Two things that will bite you

**Imports move.** `EnsembleRetriever` used to be in `langchain.retrievers`. In the installed version (1.4.3) it is in `langchain_classic.retrievers`. The first run of this port failed on exactly that. The pipeline did not change; the package layout did. Pin your versions, which is why [requirements.txt](requirements.txt) has exact numbers and a date.

**`langchain-community` is being sunset.** Running this prints a deprecation warning, because `BM25Retriever` still lives there. The warning is left in on purpose. It is what using this ecosystem is actually like, and you should see it rather than read about it.

## What you may now say

You have run a LangChain retrieval pipeline and scored it. You may say that.

You have not built a LangChain agent, used LangGraph, or deployed any of this. Those are different claims. The `agent` command is still C#-only.
