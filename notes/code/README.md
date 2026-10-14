# The engineering track

The `notes/` folder answers **what is this idea and why does it matter**.

This folder answers the programmer's questions:

- Which technology or algorithm is this, by name?
- Which files, classes and methods implement it?
- What data structures, and what is the complexity?
- What would I use instead in production, and why?
- What would an interviewer ask me about the implementation?

Read the day's conceptual note first, then the file here. Concept, then code.

---

## The days

| Day | Topic | Code walkthrough | Main files |
| --- | --- | --- | --- |
| 1 | The request | [day-1-the-http-call.md](day-1-the-http-call.md) | `Chat.cs` |
| 2 | Retrieval | [day-2-retrieval.md](day-2-retrieval.md) | `Search.cs`, `Embeddings.cs` |
| 3 | Chunking and eval | [day-3-chunking-and-eval.md](day-3-chunking-and-eval.md) | `Chunking.cs`, `Evaluation.cs` |
| 4 | Structured output | [day-4-structured-output.md](day-4-structured-output.md) | `Extraction.cs` |
| 5 | Agents and tools | [day-5-agents-and-tools.md](day-5-agents-and-tools.md) | `Agent.cs` |
| 6 | The LangChain port | [day-6-langchain-port.md](day-6-langchain-port.md) | `ask.py` |
| 7 | Production gaps | [day-7-production-gaps.md](day-7-production-gaps.md) | all of them |

---

## The stack, in one table

Worth knowing before you start, because it is shorter than people expect.

| Layer | What this project uses | NuGet packages |
| --- | --- | --- |
| Runtime | .NET 8 (`net8.0`), C# 12, nullable enabled | — |
| HTTP | `System.Net.Http.HttpClient` | — |
| JSON | `System.Text.Json` + `System.Text.Json.Nodes` | — |
| Retrieval | hand-written BM25 and cosine similarity | — |
| Embeddings | hand-written feature hashing (FNV-1a) | — |
| Tests | xUnit 2.9.3 | 3, test project only |
| Python port | LangChain 1.4.3, rank-bm25 | pinned in `requirements.txt` |

**The application project has zero NuGet packages.** Everything is the base class library.

That is deliberate, and it is the single most useful thing to notice here: **nothing in a RAG pipeline requires a special AI library.** It is HTTP, JSON, string handling and some arithmetic. The libraries save you typing, not understanding.

---

## Design patterns used, and where

| Pattern | Where | Why it is there |
| --- | --- | --- |
| Interface seam | `IChatClient`, `IEmbedder` | swap real model ↔ fake, with no other change |
| Test double (stub) | `StubChatClient` | return exact text to a test |
| Test double (fake) | `ScriptedChatClient` | a working fake with real behaviour |
| Decorator | `RecordingChatClient` | observe requests without changing the loop |
| Factory | `ModelEndpoint.For` | build the right URL shape per provider |
| Allow-list dispatch | `ToolCatalog.InvokeAsync` | the model cannot invoke anything unlisted |
| Result-not-exception | tool errors | the model reads the error and recovers |

These are ordinary C# patterns. None of them is AI-specific — which is the point.
