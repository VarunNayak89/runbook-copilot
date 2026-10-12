# AI learning

**New here, or not sure what to do next? → [START-HERE.md](START-HERE.md).** Seven sessions, in order, one command each. This README is the reference; that file is the path.

This folder is a personal study space, kept separate from any work repository on purpose. The company in the sample app, Northwind Cloud, is fictional, so this can be shown publicly. Never copy real runbooks, tenant names, pipeline values, or keys from work into it.

## What you are building toward

Job posts ask for RAG, LangChain, LLM applications, and agents. Those are one pipeline with different library names:

1. Call a language model.
2. Put the right private text into the prompt (RAG).
3. Make the model return JSON your code can validate.
4. Let the model call tools in a loop (an agent).

## The two projects

| Project | What it is |
| --- | --- |
| [Runbook Copilot](projects/RunbookCopilot/README.md) | A .NET 8 console app: retrieval, eval, grounded prompts, structured output, and a tool-calling loop. The same shape as the tools you already write — options, files, HTTP, and tests. |
| [LangChain port](projects/langchain-port/README.md) | The `ask` path rebuilt in Python with the real LangChain library. Same corpus, same constants, same 8/8. It exists so "I have used LangChain" is a true sentence and you can say what the library did and did not decide. |

A local model is installed (Ollama, `llama3.2`). Both projects read the same `COPILOT_*` environment variables, so one block configures both:

```powershell
$env:COPILOT_PROVIDER = "OpenAICompatible"
$env:COPILOT_ENDPOINT = "http://localhost:11434/v1"
$env:COPILOT_CHAT_MODEL = "llama3.2"
```

Leave them unset and both projects print the retrieved passages and stop, which is the honest offline mode.

## Read in this order

| Order | Note | You can explain |
| --- | --- | --- |
| 1 | [How to study](notes/00-how-to-study.md) | What you are allowed to say in an interview, and what you are not |
| 2 | [How an LLM works](notes/01-how-llms-work.md) | Tokens, context, temperature, why the model invents facts |
| 3 | [Prompts and JSON](notes/02-prompts-and-structured-output.md) | System message, grounding, and why a validator sits behind the model |
| 4 | [RAG](notes/03-rag.md) | Chunk, embed, search, cite, and how recall@3 is different from a good answer |
| 5 | [Agents](notes/04-agents.md) | Tool calls, the loop, and when a fixed pipeline is the better product |
| 6 | [Library names](notes/05-libraries.md) | How LangChain and Semantic Kernel map onto this C# app |
| 7 | [Production](notes/06-production.md) | Keys, injection, cost, and actions that must stay human |
| 8 | [Interview](notes/07-interview.md) | A two-minute story tied to files you can open |
| 9 | [The request body](notes/08-the-request-body.md) | All four words — RAG, LLM app, agent, LangChain — read off one printed HTTP request |

Write your check-yourself answers in [notes/my-answers](notes/my-answers/README.md). Saying it in writing is the study. Reading is the setup.

## Tonight

From `projects/RunbookCopilot`:

```powershell
dotnet test
dotnet run --project src/RunbookCopilot -- eval
dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
dotnet run --project src/RunbookCopilot -- agent --scripted "Is billing-api healthy and where is failover documented?"
dotnet run --project src/RunbookCopilot -- extract "Create a trial tenant for Contoso in eastus, environment dev, ending on 2026-11-01"
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
dotnet run --project src/RunbookCopilot -- trace agent "Is billing-api healthy and where is failover documented?"
```

`trace` sends nothing. It prints the HTTP request body the other commands would post. If one command makes the four words concrete, it is that one — read it with [notes/08-the-request-body.md](notes/08-the-request-body.md) open.

Then the LangChain port, from `projects/langchain-port`:

```powershell
.\.venv\Scripts\python.exe ask.py --eval
.\.venv\Scripts\python.exe ask.py "Which region is paired with eastus for failover?"
```

And with the model running, the run that found the three mistakes in [notes/my-answers/lab-6-transcript.md](notes/my-answers/lab-6-transcript.md):

```powershell
dotnet run --project src/RunbookCopilot -- agent "Is billing-api healthy and where is failover documented?"
dotnet run --project src/RunbookCopilot -- extract "Create a tenant for Fabrikam in mars, environment prod"
```

The second one is accepted, and it should not be. That is lab 7.

Then read [notes/03-rag.md](notes/03-rag.md) and answer its questions in your own words. The labs in [projects/RunbookCopilot/LABS.md](projects/RunbookCopilot/LABS.md) are how this becomes experience you can claim.
