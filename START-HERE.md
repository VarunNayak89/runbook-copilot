# Start here

## Step zero: the five words, in easy English

If the words LLM, RAG, agent and LangChain are new to you, read this first:

### → [notes/ai-in-simple-words.md](notes/ai-in-simple-words.md)

It uses short sentences. It explains each word with an example you already know from normal software development. Read one part, stop, then read the next part.

Do not read anything else until that note makes sense. Everything below assumes it.

---

## After that: one command, one idea

```powershell
cd projects\RunbookCopilot
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
```

Read the JSON it prints. That object is most of what a job post means by "AI engineering." Everything else in this folder is a consequence of it.

## The seven sessions

One session is 30 to 45 minutes. Do one a day and you are done in a week. Do not skip ahead; each one is the reason the next one makes sense.

The rule for every session: **run the command first, form an opinion, then read the note.** A note you read before running the command teaches you words. A note you read after teaches you the thing.

| # | Session | Run this | Then read | You will be able to say |
| --- | --- | --- | --- | --- |
| 1 | An LLM call is a POST | `trace ask` | [note 8](notes/08-the-request-body.md) | Why the model cannot see your files, and why it has no memory |
| 2 | RAG is putting text in a string | `eval`, then `ask` | [note 3](notes/03-rag.md) | Why a retrieval miss cannot be fixed by a better prompt |
| 3 | Retrieval is the hard part | lab 2, then lab 3 | [note 3](notes/03-rag.md) again | What chunk size costs you, measured, not guessed |
| 4 | The model will lie, fluently | lab 6 commands | [note 2](notes/02-prompts-and-structured-output.md), [note 1](notes/01-how-llms-work.md) | Why a validator accepted a region nobody asked for |
| 5 | An agent is a loop you wrote | `trace agent`, then lab 4 | [note 4](notes/04-agents.md) | Why the model never executes anything |
| 6 | LangChain is a wrapper | the [port](projects/langchain-port/README.md) | [note 5](notes/05-libraries.md) | What the library decided, and what it left to you |
| 7 | Shipping and saying it | lab 7 | [note 6](notes/06-production.md), [note 7](notes/07-interview.md) | The two-minute story, with the app closed |

## The loop that makes it stick

For each session:

1. Run the command. Look at the output before you have an explanation for it.
2. Read the note.
3. **Write the answers to that note's "Check yourself" questions in `notes/my-answers/`, in your own words, without the note open.**
4. Bring that file back to me. I correct what is wrong or vague.

Step 3 is the session. Steps 1 and 2 are setup. If you skip step 3 you will recognise these ideas and not be able to produce them, and an interview asks you to produce them.

If you cannot answer a question, that is not failure. That is the session telling you which part to re-run.

## What you are allowed to claim

Read [notes/00-how-to-study.md](notes/00-how-to-study.md) once, now, before session 1. It is short. It is the difference between a story that survives follow-up questions and one that collapses.

Short version: say the level you are actually on. Never describe this as work you shipped at your day job.

## If you only have ten minutes today

Run session 1's command and answer this one question in writing:

> The runbook text is inside the JSON. Where exactly, and what does that tell you about what the model can and cannot see?

That single question is the foundation of RAG, agents, prompt injection, and cost. Everything else is built on it.
