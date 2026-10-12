# Library names

Job posts say LangChain, LlamaIndex, Semantic Kernel, Azure AI Search, and "agents." They are naming boxes around the same pipeline you have in C#.

Learn the pipeline until you can draw it. Then the library is a weekend of reading docs, not a new career. If you want proof that the libraries are wrappers, run `trace` and read [note 8](08-the-request-body.md) first: every one of them ends up building the same `messages` array and posting it. This note is the map. It is not a LangChain certificate. Do not put LangChain on a resume until a Python port of `ask` runs on your machine.

## LangChain, and the file it matches

LangChain is a Python and JavaScript toolkit. The names move between versions. The jobs do not.

| They say | It does | This app |
| --- | --- | --- |
| Document loader | Read files or pages | `KnowledgeBase.LoadAsync` |
| Text splitter | Chunk | `MarkdownChunker` |
| Embeddings | Vectors | `IEmbedder`, `HashingEmbedder`, `OpenAiEmbedder` |
| Vector store | Save and search vectors | `VectorIndex` (memory). At scale, Azure AI Search or similar |
| Retriever | Return passages for a question | `HybridSearch` |
| Prompt template | Fill a prompt | `GroundedPrompt` |
| LLM / chat model | Generate | `OpenAiChatClient` |
| Output parser | Turn text into an object | `TenantRequestReader` plus `TenantRules` |
| Tool | A function the model can call | `ToolCatalog` |
| Agent executor | The loop | `AgentLoop` |
| LangSmith | Traces and evals | `RetrievalEval` is the small version of the eval half |

LlamaIndex is the same story with more emphasis on the index and the query. If a post says LlamaIndex and another says LangChain, the design questions are the same: chunk size, what you retrieve, how you grade it.

## .NET names you will actually ship

`Microsoft.Extensions.AI` is the shared .NET interface for chat and embeddings (`IChatClient`, `IEmbeddingGenerator`). This project uses its own small interfaces so you can see the HTTP. The idea matches: one seam, several backends.

Semantic Kernel is Microsoft's toolkit for plugins (tools), planners, and prompts. A Semantic Kernel plugin is a tool. You do not need it to understand agents. You might use it later so you write less HTTP.

Azure AI Search is a hosted index: keyword plus vector plus filters (region, product, date). Your `HybridSearch` is the teaching version. The filter half matters in a multi-tenant product: retrieve this customer's docs, not everyone's. This sample has one corpus and no tenant filter. Be able to say that gap.

Azure OpenAI is the host for the model. The URL is different from public OpenAI and from Ollama. `ModelEndpoint.For` builds both, and a test locks the Azure shape: deployment name in the path, `api-version` on the query, `api-key` header. That URL is a common real bug.

## What to say when the post requires LangChain

The port now exists: [projects/langchain-port](../projects/langchain-port/README.md). Same corpus, same embedder, same RRF constant, same eval. Both score 8/8.

"I built the RAG and tool-calling pipeline in C#, then rebuilt the retrieval path in LangChain to check I understood the design rather than my own code. The library wrote the plumbing. It decided none of the chunking, retrieval, prompt, or eval choices. I have not built a LangChain agent or used LangGraph."

Two things the port taught that this note predicted in the abstract:

- **The imports move.** `EnsembleRetriever` was in `langchain.retrievers`. In 1.4.3 it is in `langchain_classic.retrievers`, and the first run failed on that line. `langchain-community`, where `BM25Retriever` still lives, now prints a sunset warning. The pipeline did not change. The packages did. Pin versions.
- **Same design, different ranks.** Identical constants and corpus, and the C# and Python put different files at positions 3 and 4. Recall@3 hides it. Read the port's README for the table.

## Check yourself

1. Which file is the retriever, and which file is the agent executor?
2. What is the difference between Azure OpenAI's URL and `http://localhost:11434/v1/chat/completions`?
3. Name two things LangChain wrote for you in the port, and two decisions it left entirely to you.
4. The two implementations rank passages 3 and 4 differently. Give the reason, and say why recall@3 did not notice.
