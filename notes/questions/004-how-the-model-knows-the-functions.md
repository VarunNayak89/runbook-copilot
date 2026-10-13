# 004 — How does the model know which functions exist?

**Asked on 2026-10-13, after `trace agent`.**

> I don't understand how RAG is sending functions to the LLM. Does it by default send all the functions? When it doesn't know which function to call, how is it sending those functions?

---

## First, a mix-up worth fixing

**RAG never sends functions.** `ask` and `agent` are two different commands that work differently.

Prove it:

```powershell
dotnet run --project src/RunbookCopilot -- trace ask   "Is billing-api healthy?"
dotnet run --project src/RunbookCopilot -- trace agent "Is billing-api healthy?"
```

| | `ask` (RAG) | `agent` (tools) |
| --- | --- | --- |
| Functions sent | **none at all** | 3, in every request |
| Who finds the documents | **your code**, before calling the model | the **model** asks for it |
| What the model may do | only answer | pick a function, or answer |
| API calls | 1 | 1 per turn |

In `ask`, your code searches first and pastes the text into the prompt. The model never chooses anything. That is RAG.

---

## The answer: yes, all of them, every time

Run `trace agent` and look at the `tools` array in each of the three requests:

```
Request 1 of 3  ->  search_runbooks, get_service_status, get_runbook
Request 2 of 3  ->  search_runbooks, get_service_status, get_runbook
Request 3 of 3  ->  search_runbooks, get_service_status, get_runbook
```

Identical. All three, three times.

### "But how does my code know which one to send?"

It does not need to know. That is the whole trick:

> **Your code sends the complete list. The model picks one. Your code runs what it picked.**

Your code never chooses a function. It only *offers* and then *executes*.

---

## The menu

You walk into a restaurant. The waiter does **not** guess what you want. He hands you the **entire menu** — every dish, with a description under each one.

You read it and say: *"I'll have the fish."*

Then the **kitchen** cooks it. You cannot cook. You can only name a dish that is on the menu.

| In the restaurant | In the code |
| --- | --- |
| The menu | `ToolCatalog.Definitions` — your code writes it |
| The customer reading it | the model |
| Saying "I'll have the fish" | the model returns `tool_calls` |
| The kitchen | `ToolCatalog.InvokeAsync` — your code runs it |

The waiter never knows your order in advance. He does not need to. He brings the menu.

And note: a dish that is **not on the menu cannot be ordered**. If a function is not in `Definitions`, the model does not know it exists.

---

## How the model actually chooses

It only ever sees three fields per function:

```json
{
  "name": "get_service_status",
  "description": "Read the current status of one Northwind service.",
  "parameters": {
    "service": { "type": "string", "description": "Service id, for example billing-api" }
  }
}
```

**The `description` is what it reads to decide.** That is a real engineering lesson:

> Write tool descriptions the way you would write them for a new colleague on their first day. A vague description makes the model pick the wrong tool, and that is your bug, not the model's.

Notice what it never receives: **your C# code**. It cannot execute anything. It can only emit a name and an arguments string. Everything after that is your program's decision.

---

## Why resend all three every turn?

Same reason the messages grow ([question 003](003-why-messages-grow.md)): **the server remembers nothing.**

Turn 2 has no idea what turn 1 sent. If you left the menu out of turn 2, the model would not know any functions existed at all.

So every request carries both: the full conversation **and** the full menu.

### This costs real money

The three tool definitions here are roughly 500 tokens. Sent 3 times, that is ~1,500 tokens just describing functions — before any actual content.

With 30 tools you would be paying for a very large menu on every single turn. That is why production systems eventually **filter the tool list** before sending: show the model only the handful of tools that could plausibly apply to this request.

We do not do that. One more honest gap to name.

---

## Say this out loud

> "I don't choose the tool. I send the whole list of function definitions on every turn — name, description, and a JSON schema — and the model replies with a name and arguments. My C# looks that name up and runs it. The model never gets my code and has no permissions. The tool descriptions are part of the prompt, so I write them carefully, and I pay for all of them on every turn."

---

## Check yourself

1. How many tool definitions does `trace ask` send? Why?
2. If you add a fourth function to `ToolCatalog.Definitions` but not to `InvokeAsync`, what happens when the model asks for it?
3. Why must the tools array be resent on turn 3, when it was already sent on turns 1 and 2?

---

## Related

- [notes/04-agents.md](../04-agents.md) — the agent note
- [question 003](003-why-messages-grow.md) — why the messages grow
- [Agent.cs](../../projects/RunbookCopilot/src/RunbookCopilot/Agent.cs) — `ToolCatalog`, the menu and the kitchen
