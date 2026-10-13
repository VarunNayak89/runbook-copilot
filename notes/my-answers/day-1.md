# Day 1 — An LLM call is just a POST

Note: [08-the-request-body.md](../08-the-request-body.md)
Command run: `dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"`
Date: 2026-10-13

---

## Question 1

> Open the `trace ask` output. Which exact JSON field holds the runbook text, and what does that tell you about why a retrieval miss cannot be fixed by editing the system prompt?

### My first answer

> `<message>` `<content>` under passages field has runbook text.
> If retrieval miss to grab correct passage, prompt can't help to answer as this answer hold inside our project and only retrieval can give copy of that to LLM, LLM don't have that data.

### Marking

**Second half: correct.** This is the harder half and the reasoning is right. "Only retrieval can give a copy of that to the LLM, the LLM doesn't have that data" is exactly the idea.

**First half: nearly right, with one important mistake.** I said "under passages field". **There is no `passages` field.**

Look at the actual JSON:

```json
"messages": [
  { "role": "system", "content": "You answer questions about Northwind Cloud operations.\r\n..." },
  { "role": "user",   "content": "Passages:\r\n[1] failover.md — Regional failover\r\n..." }
]
```

There are only **two** fields on each message: `role` and `content`. That is all.

The word `Passages:` is not a field name. It is **text I typed inside the content string**, in `GroundedPrompt.cs`:

```csharp
user.AppendLine("Passages:");
```

So the exact answer is: **`messages[1].content`** — the second item in the `messages` array, the one whose `role` is `"user"`.

### Why that mistake matters

It is not a small wording error. It is the difference between two mental models:

| What I thought | What is actually true |
| --- | --- |
| The JSON has a structured `passages` field | The JSON has one long **string** |
| The model receives a list of 4 documents | The model receives **prose that looks like a list** |
| The model knows passage 3 came from `incidents.md` | It knows that only because I typed those letters next to the text |

The `[1]` `[2]` `[3]` numbers and the file names look like structure. They are characters. Nothing connects the label `failover.md` to the sentences under it except that they are near each other in a string.

**This is why a citation is not proof.** When the model writes `[failover.md]` next to a sentence, it is copying a string it saw nearby. It is not looking anything up. That is exactly how, on Day 5, we get a correctly-cited answer whose sentence is not in that file.

### The corrected answer, as I would say it in an interview

> The runbook text sits in `messages[1].content` — inside the user message, as plain string content. There is no separate field for it; the passage numbering and the file names are just text I formatted into that one string.
>
> That is why editing the system prompt cannot fix a retrieval miss. The system prompt changes the **instructions**. It does not change the **content**. If retrieval picked the wrong passages, the fact is simply not in the request at all, and the model cannot read something that was never sent. No wording makes an absent string appear.
>
> It is also why I measure retrieval on its own, with `eval`, before I look at any generated sentence.

---

## Question 2

> A user chats for 50 messages. What is sent on message 50, and what does that do to your cost?

*(not answered yet)*

---

## Question 3

> Why is `get_runbook` refusing `..` a stronger control than telling the model to behave?

*(not answered yet)*

---

## What I take from Day 1

- An LLM call is one HTTP POST with a `messages` array. There is nothing else to the interface.
- The model's only access to my documents is the text I copy into `content`.
- `role` and `content` are the only fields on a message. Any structure I see inside `content` is formatting I did myself.
- Retrieval sets the ceiling. Generation cannot rise above what retrieval put in the request.
