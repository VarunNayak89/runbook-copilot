# 002 — Can I say "I built a RAG system"?

**Asked on 2026-10-13, after finishing Day 1.**

> Based on Day 1, can I say in an interview I built a RAG system, or RAG code to call an LLM?
> And can I give an example like: at work our clients check loan eligibility, and I developed something similar as a C# library?

---

## Short answer

**Not yet, and not that way.**

After Day 1 you have run one command and understood one idea. That is real progress and it is not "I built a RAG system."

The second part is more serious. Do not connect this study project to your employer's product. It would be false, and it would be caught.

**But the honest version of your story is stronger than the inflated one.** It is written at the bottom of this page.

---

## Part 1 — Why "I built it" is not true yet

### The fact you must not forget

This code was written **with an AI assistant, in a chat session**. You guided it, you asked the questions, you chose the direction. You did not type the code.

And that is **public in your own repository**:

```
commit 53ec7f2
Author: Varun Nayak <VarunNayak89@users.noreply.github.com>
    Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
```

Every commit says this. Anyone who opens your GitHub can see it in ten seconds. If you say "I wrote this" and they look, the conversation is over — not because using AI is wrong, but because you were not straight about it.

Keep the trailers. They are honest, and in 2026 "I built this with an AI assistant and I can explain every line" is a completely respectable sentence. "I wrote every line myself" is a sentence your own git history contradicts.

### What an interviewer actually does with "I built a RAG system"

They do not take it at face value. They ask follow-ups:

| They ask | Could you answer it today? |
| --- | --- |
| "Why that chunk size?" | No — you have not run Lab 3 |
| "What was your recall, and what does it miss?" | Partly — you have seen 8/8, not yet why it is misleading |
| "Why hybrid search and not just vectors?" | Not yet — that is Day 2 |
| "What broke when you changed something?" | No — you have not changed anything yet |
| "Walk me through a tool call." | Not yet — that is Day 5 |

"I built it" is a promise that you can answer all of these. Three questions in, a vague answer is worse than never making the claim.

### What is true today, after Day 1

> "I am working through a RAG project to learn how LLM applications are actually built. So far I understand that an LLM call is one HTTP POST with a messages array, and that retrieved documents travel as plain string content inside it — which is why a retrieval miss cannot be fixed by changing the prompt."

That is modest, specific, and **completely true**. A good interviewer respects it far more than a confident claim that collapses.

---

## Part 2 — Why the work example is a trap

You suggested saying something like: *at work, clients check loan eligibility; I developed something similar as a C# library.*

Three separate problems, any one of which ends the interview badly.

### 1. The technologies are opposites

| A rules engine deciding loan eligibility | A RAG application |
| --- | --- |
| Deterministic — same input, same output, always | Probabilistic — the same question can give a different answer |
| Auditable — you can show exactly which rule fired | Hard to audit — you can show the passages, not the reasoning |
| No model, no embeddings, no retrieval | The model is the whole point |
| Wrong output is a **bug** | Wrong output is **expected** and must be designed around |

Saying one is "similar" to the other tells an interviewer you do not really understand either. Anyone who has used a rules engine will notice immediately.

### 2. It implies you shipped AI at work

You have not. Your own notes say this in four places. If a hiring manager calls your employer, or if the interviewer has worked there, this is checkable and it looks like a lie — even if you only meant it loosely.

### 3. Client use cases are not yours to describe

What customers use your employer's product for is their business, not interview material. Keep examples generic.

---

## Part 3 — The honest story, which is better

Here is the thing: **there is a real and impressive connection between your day job and this project.** It is just not the one you reached for.

Your employer's world is **deterministic decisioning** — rules that must give the same answer every time and be auditable. LLMs are the exact opposite. The interesting engineering question is **where you put the boundary between them.**

That is not a small observation. It is the central design question in production AI, and most candidates cannot articulate it.

### Say it like this

> "My day job is C# — deterministic decisioning, validation, allowlists, API clients. Rules that have to give the same answer every time and be auditable.
>
> I started learning LLM applications on my own time because that world is the opposite: the same question can produce a different answer. I wanted to understand where the boundary belongs.
>
> So I worked through building a RAG application in .NET — chunking, hybrid retrieval, a recall eval, and a read-only tool loop — using an AI assistant, and I can walk you through any part of it.
>
> The thing that stayed with me is where deterministic code still has to win. I asked my extractor for a tenant in region 'mars', which is not on the allowlist. The model did not return an invalid value — it quietly returned 'westeurope' instead, and my validator accepted it, four runs out of four. The allowlist caught malformed output but not confident invention, because nothing compared the value back to the input sentence.
>
> That is the same instinct as a rules engine. The model proposes. Deterministic code decides."

### Why that version is stronger

- Every sentence is true and you can defend it
- It shows **judgment**, not just tool knowledge
- The `mars` story is specific, memorable, and shows you find your own bugs
- "The model proposes, the application decides" is the correct production principle — it is in the guardrails section of [the production note](../reference/production-genai-systems.md)
- It connects your existing experience honestly, instead of inventing new experience

Candidates who claim everything are common. Candidates who say precisely what they did and did not do are rare, and they get hired.

---

## Part 4 — How to make "I built it" true

It takes about a week, and [START-HERE.md](../../START-HERE.md) is the path.

| After | You can honestly say |
| --- | --- |
| **Day 1** (done) | "I understand what an LLM application sends on the wire." |
| **Day 2** | "I understand how retrieval works and how I measure it." |
| **Day 3** (Labs 2–3) | "I changed the corpus and the chunk size and I can tell you what the score did." ← **this is where it starts becoming yours** |
| **Day 4** | "I connected a real model and found a case where my validator accepted a fabricated value." |
| **Day 5** (Lab 4) | "I added a tool to the agent loop and tested it." |
| **Day 6** | "I ran the same pipeline in LangChain and compared the two." |
| **Day 7** (Lab 7) | "I found a validation gap and fixed it." ← **now "I built this" is defensible** |

The line is not about typing the code. It is this:

> **Can you defend every decision, and did you change something and measure the result?**

After Lab 3 and Lab 7 the answer is yes, and then you may say you built it — with the honest footnote that you used an AI assistant, which is what every working engineer does in 2026.

---

## The rule to remember

> **Claim the level you are actually on. Say the specific thing you found.**
>
> A small true story beats a big vague one, because the interview is a conversation and the follow-up question always comes.

---

## Related

- [notes/00-how-to-study.md](../00-how-to-study.md) — the three levels
- [notes/07-interview.md](../07-interview.md) — the full two-minute story and the resume line
- [notes/my-answers/lab-6-transcript.md](../my-answers/lab-6-transcript.md) — the `mars` finding in full
