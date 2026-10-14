# START HERE

**This is the only file you need to open.** It will tell you what to read and what to run, in order. Every link goes to the right place at the right time.

Do not browse the other folders. Just follow the steps below.

---

## Before you begin (20 minutes, once)

Do these two things first. They are not optional, and they are short.

### Step 1 — See what is in the folder

→ **[PROJECT-MAP.md](PROJECT-MAP.md)**

It shows the folder tree and says what each file is for. After reading it, you can ignore 80% of the files.

### Step 2 — Learn the five words

→ **[notes/ai-in-simple-words.md](notes/ai-in-simple-words.md)**

Easy English, short sentences. It explains:

- **LLM** — a web API. Text in, text out.
- **LLM application** — a normal program. One step calls the LLM.
- **RAG** — find the right pages, paste them into the question.
- **Agent** — a loop. The model asks for a function, your code runs it.
- **LangChain** — a library for the boring parts.

**Do not continue until these five words make sense.** Everything after this assumes them.

### Step 3 — Open a terminal here

```powershell
cd C:\Users\VarunNayak\Personal\AI-Learning\projects\RunbookCopilot
```

Keep this window open. Every command below runs from this folder.

---

# The 7 days

One day is **30 to 45 minutes**. Do one a day and you finish in a week.

Each day has the same parts:

> **RUN** a command → **READ** a note → **CODE** the engineering detail → **WRITE** your answers

**RUN / READ / WRITE** teaches you the idea. **CODE** is the programmer's version of the same day: the algorithms by name, the exact classes and methods, the data structures and complexity, and what you would use instead in production. Skip it if you only want the concept; read it if you want to defend the implementation in an interview.

The whole engineering track is indexed in [notes/code/](notes/code/README.md).

The **WRITE** part is the one that matters. Reading feels like learning. Writing *is* learning. An interview asks you to produce an explanation, not to recognise one.

---

## Day 1 — An LLM call is just a POST

**RUN**

```powershell
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
```

Nothing is sent anywhere. It prints the JSON that *would* be sent. Look at it before you read anything.

**READ** → [notes/08-the-request-body.md](notes/08-the-request-body.md)

**CODE** → [day-1-the-http-call.md](notes/code/day-1-the-http-call.md) — `System.Text.Json.Nodes` vs `JsonDocument`, the `IChatClient` seam, stub vs fake vs decorator, and `Microsoft.Extensions.AI` for production.

**WRITE** → `notes/my-answers/day-1.md`

1. Which exact JSON field holds the runbook text?
2. Why can a retrieval mistake not be fixed by changing the system message?
3. A user chats for 50 messages. What is sent on message 50?

**Bonus question already answered for you:** [Why is it POST and not GET?](notes/questions/001-why-post-not-get.md)

---

## Day 2 — RAG: finding the right pages

**RUN**

```powershell
dotnet run --project src/RunbookCopilot -- eval
dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
```

`eval` scores the **search only**. No model is called. Look at the number it prints.

**READ** → [notes/03-rag.md](notes/03-rag.md)

**CODE** → [day-2-retrieval.md](notes/code/day-2-retrieval.md) — Okapi BM25 (k₁, b), the hashing trick with FNV-1a, cosine similarity, brute-force k-NN vs HNSW, and the RRF arithmetic with real numbers.

**WRITE** → `notes/my-answers/day-2.md`

1. What does `Recall@3: 8/8` mean — and what does it **not** tell you?
2. Look at the 4 passages from `ask`. One of them does not answer the question. Which one, and why was it returned?
3. Why does the app use keyword search **and** vector search, instead of just one?

---

## Day 3 — Break it on purpose

Today you change the code. This is where it stops being someone else's project.

**RUN** → do **Lab 2** and **Lab 3** in [projects/RunbookCopilot/LABS.md](projects/RunbookCopilot/LABS.md)

- Lab 2: add your own document and your own test question
- Lab 3: make the chunks tiny (200 characters) and watch the score drop

**READ** → [notes/03-rag.md](notes/03-rag.md) again, the section "Chunk size is a trade"

**CODE** → [day-3-chunking-and-eval.md](notes/code/day-3-chunking-and-eval.md) — the sliding window with overlap and the infinite-loop invariant, `yield return`, why `.Any()` means recall, and MRR/nDCG for production.

**WRITE** → `notes/my-answers/day-3.md`

1. Which question failed when you made chunks small? Why exactly?
2. Was the fact cut in half, or was it simply not found?
3. What did you put the chunk size back to, and how did you decide?

---

## Day 4 — The model will lie to you, confidently

The most important day. Make sure the local model is running.

**RUN**

```powershell
$env:COPILOT_PROVIDER = "OpenAICompatible"
$env:COPILOT_ENDPOINT = "http://localhost:11434/v1"
$env:COPILOT_CHAT_MODEL = "llama3.2"

dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
dotnet run --project src/RunbookCopilot -- ask "What is the maximum number of virtual machines per tenant?"
dotnet run --project src/RunbookCopilot -- extract "Create a tenant for Fabrikam in mars, environment prod"
```

Watch the third one carefully. You asked for region **mars**. Look at what comes back, and look at whether it was **Accepted**.

**READ** → [notes/02-prompts-and-structured-output.md](notes/02-prompts-and-structured-output.md), then [notes/my-answers/lab-6-transcript.md](notes/my-answers/lab-6-transcript.md)

**CODE** → [day-4-structured-output.md](notes/code/day-4-structured-output.md) — fence stripping, allow-list validation, why a regex baseline exists, and why provider JSON Schema still would not have caught `mars`.

**WRITE** → `notes/my-answers/day-4.md`

1. What region did the model return for "mars"? Why is that **worse** than being rejected?
2. The validator has a test and the test passes. So why did a wrong value get through?
3. What check is missing? (Think: what did nobody compare?)

---

## Day 5 — Agents: a loop you wrote

**RUN**

```powershell
dotnet run --project src/RunbookCopilot -- trace agent "Is billing-api healthy and where is failover documented?"
dotnet run --project src/RunbookCopilot -- agent --scripted "Is billing-api healthy and where is failover documented?"
dotnet run --project src/RunbookCopilot -- agent "Is billing-api healthy and where is failover documented?"
```

The last one uses the real model. Compare it to the scripted one — **they behave differently**.

**READ** → [notes/04-agents.md](notes/04-agents.md), then the comments in [Agent.cs](projects/RunbookCopilot/src/RunbookCopilot/Agent.cs)

**CODE** → [day-5-agents-and-tools.md](notes/code/day-5-agents-and-tools.md) — allow-list dispatch instead of reflection, errors as results, the path-traversal guard (and the stricter version), `for` not `while`, and MCP / Semantic Kernel.

**WRITE** → `notes/my-answers/day-5.md`

1. In `trace agent`, the message count goes 2, 4, 6. What two messages are added each round?
2. Can the model run a C# function by itself? Explain what actually happens.
3. Why does the loop have `maxSteps = 4`?

---

## Day 6 — LangChain

**RUN**

```powershell
cd ..\langchain-port
.\.venv\Scripts\python.exe ask.py --eval
.\.venv\Scripts\python.exe ask.py "Which region is paired with eastus for failover?"
cd ..\RunbookCopilot
```

Same documents. Same score. Different language, different library.

**READ** → [projects/langchain-port/README.md](projects/langchain-port/README.md), then [notes/05-libraries.md](notes/05-libraries.md)

**CODE** → [day-6-langchain-port.md](notes/code/day-6-langchain-port.md) — the class-for-class mapping, why Python's FNV-1a needs `& 0xFFFFFFFF`, the import that moved in 1.x, and which library a .NET team should actually pick.

**WRITE** → `notes/my-answers/day-6.md`

1. Name two things LangChain wrote for you.
2. Name two decisions it did **not** make for you.
3. The C# and Python versions rank passages 3 and 4 differently. Why? And why did the score not notice?

---

## Day 7 — Say it out loud

**RUN** → do **Lab 7** in [LABS.md](projects/RunbookCopilot/LABS.md) — fix the `mars` problem you found on Day 4.

**READ** → [notes/06-production.md](notes/06-production.md) and [notes/07-interview.md](notes/07-interview.md)

**CODE** → [day-7-production-gaps.md](notes/code/day-7-production-gaps.md) — the full scorecard of what this project does **not** do, with the name of each gap and the library you would reach for. Read this one before an interview.

**WRITE** → `notes/my-answers/day-7.md`

1. Say the two-minute story **out loud, with every file closed.** Where did you stop? That is the note to re-read.
2. Which sentences are you **not** allowed to say yet?
3. Where did you put the new check, and why there?

---

## Day 8 (bonus) — What a real production system needs

The 7 days teach you to **build** it. This one teaches you what is still missing when the demo works: fine-tuning, proper eval vocabulary, observability, caching, feedback loops and guardrails.

**READ** → [notes/reference/production-genai-systems.md](notes/reference/production-genai-systems.md)

It is the longest note here. Read it in two sittings if you need to. The last section scores our own project honestly — which gaps we have, and the paragraph to say out loud about them.

**WRITE** → `notes/my-answers/day-8.md`

1. A colleague says "the model does not know our new pricing, let's fine-tune it." What do you say?
2. `pass@3` is 95% but `pass^3` is 40%. Can you ship it unattended?
3. Name two guardrails our project is missing.

---

# After the 7 days

| You want to | Go to |
| --- | --- |
| Go deeper on one topic | [PROJECT-MAP.md](PROJECT-MAP.md) — the notes table |
| Keep practising | [LABS.md](projects/RunbookCopilot/LABS.md) — labs 4, 5, 8 |
| Re-read an answer I gave you | [notes/questions/](notes/questions/README.md) |
| Prepare for an interview | [notes/07-interview.md](notes/07-interview.md) |
| Read outside material | [notes/reference/](notes/reference/README.md) |

---

# Rules of this course

**1. Run the command before you read the note.**
Form your own impression first. A note read beforehand teaches you words. A note read afterwards teaches you the thing.

**2. Write with the note closed.**
If you cannot answer, that is not failure. It tells you exactly which part to run again.

**3. Ask me anything.**
Every question you ask becomes a file in [notes/questions/](notes/questions/README.md). There is no stupid question. A question you do not ask becomes a gap you cannot explain in an interview.

**4. Only claim what you have actually done.**
See [notes/00-how-to-study.md](notes/00-how-to-study.md). Never describe this as work you shipped at your day job. An interviewer finds out in two follow-up questions, and the honesty is worth more than the claim.

---

## If you only have 10 minutes today

Run the Day 1 command. Answer this one question in writing:

> The runbook text is inside the JSON. **Where exactly?** And what does that tell you about what the model can and cannot see?

That single question is the foundation of RAG, agents, prompt injection and cost. Everything else is built on top of it.
