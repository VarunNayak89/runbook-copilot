# Learning how LLM applications actually work

A study project for a software developer who is new to AI.

It answers four questions that appear in every job posting — **what is RAG, what is an LLM application, what is an agent, what is LangChain** — by building the thing, running it, and measuring it.

---

## → Start at [START-HERE.md](START-HERE.md)

That file is the only one you need to open. It tells you what to read and what to run, day by day, and links to everything else at the right moment.

If you want to see what is in the folder first: [PROJECT-MAP.md](PROJECT-MAP.md)

---

## What is here

**[projects/RunbookCopilot](projects/RunbookCopilot/README.md)** — a .NET 8 console app that answers questions about a fictional operations guide.

- Chunks markdown by heading, with overlap so a fact on a boundary is not lost
- Hybrid retrieval: BM25 for exact words, embeddings for meaning, combined with reciprocal rank fusion
- `recall@3` eval over a fixed question set, which also runs as a unit test
- Grounded prompts that must cite a file and are allowed to say "I do not know"
- Structured output into validated JSON with an allowlist
- A read-only tool-calling agent loop with a step limit and a path check
- A `trace` command that prints the HTTP request body **instead of sending it**

**[projects/langchain-port](projects/langchain-port/README.md)** — the same retrieval path rebuilt in Python with LangChain. Same documents, same score. It exists to show that the library is plumbing, not magic.

**[notes/](PROJECT-MAP.md)** — the explanations, from [easy English](notes/ai-in-simple-words.md) up to [what a production system needs](notes/reference/production-genai-systems.md).

Every source file is commented for someone who is new to this.

---

## Run it

Nothing below needs an API key or a network connection.

```powershell
cd projects/RunbookCopilot
dotnet test
dotnet run --project src/RunbookCopilot -- eval
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
```

`trace` sends nothing. It prints the JSON an LLM application would post. That one command is the fastest way to understand what all of this really is.

For generated answers, point it at a local [Ollama](https://ollama.com) or an Azure OpenAI endpoint — see the [project README](projects/RunbookCopilot/README.md).

---

## Three things this project found

Running it against a real model (`llama3.2`, local) produced failures worth more than the features:

1. **A validator accepted a fabricated value.** Asked to extract a tenant for region `mars` — not in the allowlist — the model silently returned `westeurope` instead, and validation passed, 4 runs out of 4. An allowlist catches malformed output, not confident invention.
2. **The real model ignored the scripted plan.** It called two tools in one turn and never used the search tool the stand-in always used. The loop handled it correctly, but that path had never actually been exercised.
3. **A grounded answer still overstated its source.** The runbook says *"do not fail over while a provision lock is held"*; the answer said *"a provision lock is in place."* A rule became a status claim that no tool had checked.

Written up in [notes/my-answers/lab-6-transcript.md](notes/my-answers/lab-6-transcript.md).

---

## A note on scope

The company in the sample documents, Northwind Cloud, is invented. Nothing here comes from any employer's systems or documents.

This is a study project, not production software. [notes/00-how-to-study.md](notes/00-how-to-study.md) sets out what it is honest to claim from it, and [the reference note](notes/reference/production-genai-systems.md) ends with a table of what this project does **not** do.
