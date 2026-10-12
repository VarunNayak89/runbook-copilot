# What is in this folder

You do not need to read every file. This page tells you what each one is, so you can ignore the rest.

← Back to [START-HERE.md](START-HERE.md)

---

## The short version

There are only **three kinds of thing** in this folder:

| Kind | Where | What it is |
| --- | --- | --- |
| **Notes** | `notes/` | Explanations. You read these. |
| **Code** | `projects/` | Two working apps. You run these. |
| **Your writing** | `notes/my-answers/` | Your own answers. You write these. |

That is all. Everything below is just detail.

---

## The folder tree

```
AI-Learning/
│
├── START-HERE.md            <- the only file you must open. Day by day.
├── PROJECT-MAP.md           <- you are reading this
├── README.md                <- short summary, for someone finding this on GitHub
│
├── notes/                   <- the explanations
│   ├── ai-in-simple-words.md    <- READ FIRST. Easy English, 5 words explained.
│   ├── 00 ... 08 .md            <- the deeper notes, one topic each
│   ├── reference/               <- material from outside, saved for offline reading
│   ├── questions/               <- YOUR questions, answered. Grows over time.
│   └── my-answers/              <- YOU write here. This is the real study.
│
└── projects/
    ├── RunbookCopilot/      <- the main app. C# / .NET 8.
    │   ├── LABS.md              <- exercises that change the code
    │   ├── content/             <- the fake documents it searches
    │   ├── src/                 <- the code (every file is commented)
    │   └── tests/               <- 14 tests, no API key needed
    │
    └── langchain-port/      <- the same search, rebuilt in Python with LangChain
```

---

## The notes, and when to read each one

Do **not** read these in number order. `START-HERE.md` gives the real order.

| File | Read it when you want to know | Difficulty |
| --- | --- | --- |
| [ai-in-simple-words.md](notes/ai-in-simple-words.md) | What LLM, RAG, agent and LangChain mean | **Start here** |
| [08-the-request-body.md](notes/08-the-request-body.md) | What an LLM app actually sends on the network | Easy |
| [03-rag.md](notes/03-rag.md) | How search finds the right pages, and how we score it | Medium |
| [01-how-llms-work.md](notes/01-how-llms-work.md) | Tokens, context window, temperature, hallucination | Medium |
| [02-prompts-and-structured-output.md](notes/02-prompts-and-structured-output.md) | Getting JSON out of a model, and why you validate it | Medium |
| [04-agents.md](notes/04-agents.md) | The tool loop, and why the model has no permissions | Medium |
| [05-libraries.md](notes/05-libraries.md) | LangChain, LlamaIndex, Semantic Kernel — what they map to | Easy |
| [06-production.md](notes/06-production.md) | Keys, prompt injection, cost, human approval | Medium |
| [07-interview.md](notes/07-interview.md) | What to say, and what you must not claim | Easy |
| [00-how-to-study.md](notes/00-how-to-study.md) | The honesty rules for claiming experience | Easy |
| [reference/production-genai-systems.md](notes/reference/production-genai-systems.md) | The 7 parts of a real system: fine-tuning, evals, observability, caching, feedback, guardrails | Longest |

The numbers `00` to `08` are just file names. They are not a schedule.

### `notes/reference/` — material from outside

Notes written from good articles found elsewhere, in our own words and mapped onto our code. [The folder rule](notes/reference/README.md) explains why we never paste someone else's document into a public repository, and where to keep a private copy if you want one.

---

## The code

### `projects/RunbookCopilot` — the main app (C#)

A console app that answers questions about a **fictional** company called Northwind Cloud. Nothing in it comes from any real company.

Every source file has comments written for someone new to AI. Read them in this order:

| 1 | [Program.cs](projects/RunbookCopilot/src/RunbookCopilot/Program.cs) | The 5 commands. Look at `AskAsync` — RAG in 20 lines. |
| 2 | [GroundedPrompt.cs](projects/RunbookCopilot/src/RunbookCopilot/GroundedPrompt.cs) | Short. This file **is** the "A" in RAG. |
| 3 | [Chat.cs](projects/RunbookCopilot/src/RunbookCopilot/Chat.cs) | The LLM is an HTTP client. Nothing more. |
| 4 | [Search.cs](projects/RunbookCopilot/src/RunbookCopilot/Search.cs) | This file **is** the "R". |
| 5 | [Agent.cs](projects/RunbookCopilot/src/RunbookCopilot/Agent.cs) | The tool loop, in 30 lines. |

Supporting files: `Chunking.cs` (cutting documents up), `Embeddings.cs` (text into numbers), `KnowledgeBase.cs` (the setup step), `Extraction.cs` (JSON out of a sentence), `Evaluation.cs` (scoring), `CopilotConfig.cs` (settings and the API key).

### `projects/langchain-port` — the same thing in Python

Proof that LangChain is a library, not a new technology. Same documents, same score. Read its [README](projects/langchain-port/README.md) for the side-by-side table.

---

## Where your own work goes

| Folder | What goes in it |
| --- | --- |
| `notes/my-answers/` | Your answers to the "Check yourself" questions. **This is the study.** Reading is only preparation. |
| `notes/questions/` | Questions you asked, written up as full answers. Ask me anything and it lands here. |

---

## Things you can safely ignore for now

- `projects/RunbookCopilot/RunbookCopilot.sln` — Visual Studio solution file
- `*.csproj`, `requirements.txt` — dependency lists
- `.gitignore` — tells git what not to upload
- `bin/`, `obj/`, `.venv/` — build output, created automatically, not in git

---

Ready? → [START-HERE.md](START-HERE.md)
