# 003 — Why does the message count grow? And what is maxSteps?

**Asked on 2026-10-13, while reading note 08 and `Agent.cs`.**

> I don't understand why the message count increases. Also what is this maxSteps?

---

# Part 1 — Why the messages grow

## The one reason

**The server forgets everything.**

That is the whole answer. Everything else follows from it.

When you POST to the model, the server does not save anything. No session. No user id. No history. The moment it sends the answer back, it forgets you completely.

So if you want a conversation, **you** must remember it, and **you** must send the whole thing every time.

## A picture that helps

Imagine a very clever consultant who has **amnesia**. Every time you walk into his room, he has forgotten everything — who you are, what you asked, what he said five minutes ago.

To continue a conversation with him, you carry a **notebook** and read the whole thing aloud each time you enter.

**Visit 1** — you read:

> "Here are the rules. Here is my question."

He says: *"Go and check if billing-api is healthy."*

**Visit 2** — he has forgotten. So you read **everything again**, plus what is new:

> "Here are the rules. Here is my question.
> You asked me to check billing-api.
> The answer is: billing-api is healthy."

He says: *"Now go and read failover.md."*

**Visit 3** — he has forgotten again. You read **everything again**:

> "Here are the rules. Here is my question.
> You asked me to check billing-api.
> The answer is: billing-api is healthy.
> You asked me to read failover.md.
> Here is the content of failover.md."

He says: *"Good. Here is my final answer."*

**The notebook is the `messages` array.** It gets longer every visit, because he never remembers.

## The real output

Run this:

```powershell
dotnet run --project src/RunbookCopilot -- trace agent "Is billing-api healthy and where is failover documented?"
```

Here is what the messages actually are, in each of the 3 requests:

```
================ Request 1 of 3, messages = 2
   message 1 -> role: system
   message 2 -> role: user

================ Request 2 of 3, messages = 4
   message 1 -> role: system
   message 2 -> role: user
   message 3 -> role: assistant      <-- NEW
   message 4 -> role: tool           <-- NEW

================ Request 3 of 3, messages = 6
   message 1 -> role: system
   message 2 -> role: user
   message 3 -> role: assistant
   message 4 -> role: tool
   message 5 -> role: assistant      <-- NEW
   message 6 -> role: tool           <-- NEW
```

Messages 1 and 2 are **sent three times**. Nothing is ever removed. Only added.

## Why exactly **two** new messages each time?

This is the part people miss. Each round of the loop adds a **pair**:

| New message | Role | What it is | Who wrote it |
| --- | --- | --- | --- |
| First | `assistant` | "Please run `get_service_status` for billing-api" | **The model** asked for this |
| Second | `tool` | "billing-api: status=healthy; owner=payments" | **Your C# code** produced this |

So: **the question**, then **the answer**. Two messages. That is why the count goes 2 → **4** → **6** and not 2 → 3 → 4.

## "Why save the model's own request? Can't I just send the result?"

A fair question, and the answer is no.

If you only sent the result, the conversation would read like this:

```
user: Is billing-api healthy?
tool: billing-api: status=healthy
```

The model would see an answer to a question **nobody asked**. It has no memory, remember — it does not know it asked for that. It would be confused, and most servers will reject the request outright.

So you put its own question back in, just before the answer:

```
user:      Is billing-api healthy?
assistant: (I want to run get_service_status)     <- its question
tool:      billing-api: status=healthy            <- the answer
```

Now it reads as a sensible conversation.

## The `tool_call_id` — the string that connects them

Look at the pair closely:

```json
{ "role": "assistant", "tool_calls": [ { "id": "call-status", "function": { "name": "get_service_status" } } ] },
{ "role": "tool",      "tool_call_id": "call-status",  "content": "billing-api: status=healthy" }
```

Both say **`call-status`**.

That matching string is the **only** thing linking the answer to the question. If the model asked for two functions at once, two results come back — and only the id says which is which.

Think of it like a correlation id in a message queue. Same idea, same reason.

## In the code

[`AgentLoop.RunAsync`](../../projects/RunbookCopilot/src/RunbookCopilot/Agent.cs), the two lines that do it:

```csharp
// 1. put the model's own request back into the list
messages.Add(new ChatMessage("assistant", turn.Content ?? "", ToolCalls: turn.ToolCalls));

foreach (var call in turn.ToolCalls)
{
    var output = await _tools.InvokeAsync(call.Name, call.ArgumentsJson, cancellationToken);

    // 2. put the result in, tagged with the same id
    messages.Add(new ChatMessage("tool", output, call.Id));
}
```

`messages` is an ordinary `List<ChatMessage>`. Nothing more.

## The sentence to remember

> **An agent has no memory. The "memory" is a `List<ChatMessage>` in your own process, and you re-upload the whole list on every turn.**

---

# Part 2 — What is maxSteps?

## The simple version

**It is a limit on how many times you are willing to call the model.**

```csharp
public AgentLoop(IChatClient chat, ToolCatalog tools, int maxSteps = 4)
```

```csharp
for (var step = 1; step <= _maxSteps; step++)
```

It is a normal `for` loop limit. After 4 calls, stop — no matter what.

## Why it is necessary

Look at how the loop normally ends:

```csharp
if (turn.ToolCalls.Count == 0)   // the model asked for no tools
{
    return new AgentResult(answer, steps);   // so we stop
}
```

Read that again. **The model decides when the loop ends.**

The loop keeps going while the model keeps asking for tools. It stops when the model decides to answer instead.

That should make you uncomfortable. You have written a loop whose exit condition is controlled by **something outside your program** — something that is probabilistic and can get confused.

What if it never decides to answer?

```
Model: run search_runbooks
You:   here is the result
Model: run search_runbooks
You:   here is the result
Model: run search_runbooks
...forever
```

This is not theoretical. Small models get stuck in loops like this regularly.

## What it would cost you

Each turn is one HTTP POST, and remember the notebook keeps growing:

| Turn | Messages sent | What you pay for |
| --- | --- | --- |
| 1 | 2 | small |
| 2 | 4 | more |
| 3 | 6 | more again |
| 10 | 20 | the whole history, ten times over |

You pay for **every token in every message, on every turn**. A loop that does not stop does not just hang — it bills you, repeatedly, for a conversation that keeps getting longer.

`maxSteps` is a **cost limit** and a **time limit** at the same time.

## What you already know that is the same idea

You have written this before under different names:

| You already use | It protects you from |
| --- | --- |
| `MaxAutomaticRedirections` on an HttpClient | a redirect loop |
| a retry count | retrying forever |
| `HttpClient.Timeout` | a request that never returns |
| `for (i = 0; i < max; i++)` instead of `while (true)` | an infinite loop |

`maxSteps` is exactly that instinct, applied to a model.

## What happens when the limit is hit

```csharp
return new AgentResult("Stopped because the step limit was reached.", steps);
```

It stops and **says so honestly**. It does not invent an answer, and it does not hang. "I ran out of steps" is a real, useful result.

## Why 4?

Because the tasks this app does need two or three tool calls. Four leaves a little room and no more.

It is not a magic number. It is a budget you choose by looking at what your real tasks need. Set it too low and useful work gets cut off. Set it to 100 and you have no protection at all.

## The sentence to remember

> **`maxSteps` is there because the model, not my code, decides when the loop ends. I am not willing to let something outside my program control that, so I keep my own exit.**

That answer gets noticed in an interview. Most people describe the loop. Few mention who controls its exit.

---

## Check yourself

1. Why does the count go 2 → 4 → 6 instead of 2 → 3 → 4?
2. What would break if you added the `tool` message but not the `assistant` message before it?
3. What exactly connects a `tool` message to the request it answers?
4. Who decides when the agent loop ends — and why is that a problem?

---

## Related

- [notes/04-agents.md](../04-agents.md) — the full agent note
- [notes/08-the-request-body.md](../08-the-request-body.md) — the request, field by field
- [Agent.cs](../../projects/RunbookCopilot/src/RunbookCopilot/Agent.cs) — `AgentLoop.RunAsync`, commented line by line
