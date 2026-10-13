# The request body

The four words on the job posts are RAG, LLM application, agents, and LangChain. They describe one HTTP request and what you put in it. This note is the one place you can see that, because the app will print the request instead of sending it.

```powershell
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
dotnet run --project src/RunbookCopilot -- trace agent "Is billing-api healthy and where is failover documented?"
```

Nothing is sent. `trace` builds the body with `ChatRequest.Build`, the same method `OpenAiChatClient` posts, so you are not reading a drawing of the request. You are reading the request.

## An LLM application is a POST

Run `trace ask`. The whole thing is this:

```json
{
  "model": "<COPILOT_CHAT_MODEL>",
  "temperature": 0.1,
  "messages": [
    { "role": "system", "content": "You answer questions about Northwind Cloud operations. ..." },
    { "role": "user",   "content": "Passages:\n[1] failover.md — Regional failover\n..." }
  ]
}
```

That is the entire interface. One URL, one JSON object, a list of messages, a string back. There is no session on the server. There is no handle to your file system. The next request knows nothing about this one unless you resend it.

If you already write clients for REST APIs, you have written most of an LLM application. What is new is that the response is prose instead of a schema, so your code has to decide whether to trust it.

## RAG is where the passages sit

In that body, find `messages[1].content`. The runbook text is inside it, as characters, in a string you built in `GroundedPrompt.cs`.

That is the whole mechanism. Retrieval-augmented generation means: search your own documents first, paste the winners into the string, then send. The model was never given access to `content/runbooks`. It was given a copy of four passages.

Three things follow, and they are the three things interviewers probe:

- **If retrieval misses, nothing downstream can save you.** The fact is simply not in the body. No system prompt fixes an absent string. This is why `eval` measures retrieval on its own.
- **Everything in that string costs tokens.** You pay per request, and the whole corpus would not fit at scale. Chunking exists to spend a small budget well.
- **A distractor is in there too.** The trace line at the bottom says the passages came from `failover.md`, `incidents.md`, and `regions.md`. The question was about failover. `incidents.md` rode along. You can see it sitting in the prompt, which is more convincing than being told it happens.

Note how little magic is left once you look. "We built a RAG system" means "we concatenated search results into a string field."

## An agent is the same POST in a loop, plus two keys

Run `trace agent`. The scripted stand-in drives three turns, so you get three bodies. Compare them.

Two things appear that `ask` does not have:

**A `tools` array.** Every turn carries the same three function definitions: a name, a description, and a JSON schema for the arguments. That is all the model ever receives about your tools. It cannot call them. It can only emit a name and an arguments string, and your C# decides what to do with that. `ToolCatalog.InvokeAsync` is the thing that actually runs. This is why `get_runbook` rejecting `..` is the real control and the system prompt is not.

**A growing `messages` array.** Count them: 2, then 4, then 6.

Here is what is actually in each request:

```
Request 1 of 3, messages = 2        Request 2 of 3, messages = 4        Request 3 of 3, messages = 6
  1  system                           1  system                           1  system
  2  user                             2  user                             2  user
                                      3  assistant   <- NEW               3  assistant
                                      4  tool        <- NEW               4  tool
                                                                          5  assistant   <- NEW
                                                                          6  tool        <- NEW
```

Nothing is ever removed. Messages 1 and 2 are sent **three times**.

**Why does it grow?** Because the server remembers nothing. No session, no history. The moment it answers, it has forgotten you. So if you want a conversation, you keep the whole thing yourself and resend all of it, every turn.

**Why two new messages each round, not one?** Because each round adds a question and its answer:

| New message | Role | Who wrote it |
| --- | --- | --- |
| "Please run `get_service_status` for billing-api" | `assistant` | the **model** asked |
| "billing-api: status=healthy; owner=payments" | `tool` | your **C# code** replied |

That is why the count goes 2 → 4 → 6 and not 2 → 3 → 4.

The two are linked by a shared string:

```json
{ "role": "assistant", "tool_calls": [ { "id": "call-status", "function": { "name": "get_service_status" } } ] },
{ "role": "tool",      "tool_call_id": "call-status",  "content": "billing-api: status=healthy" }
```

Both say `call-status`. That is the **only** thing connecting the answer to the question — a correlation id, the same idea you already use with message queues. If the model asks for two functions at once, two results come back, and only the id tells them apart. A test asserts this: `Agent_turns_append_a_tool_message_that_answers_a_tool_call`.

**So the memory of an agent is a `List<ChatMessage>` in your own process, which you re-upload in full on every turn.** That is why a long loop gets slow and expensive: on turn 10 you are paying for everything that happened in turns 1 to 9 as well.

That cost is why `maxSteps` exists. But there is a sharper reason too. Look at how the loop normally ends: it stops when the model returns no tool calls. **The model decides when your loop terminates.** You would never let anything else outside your program control that, and a confused model really can keep asking for tools forever. `maxSteps` is your own guaranteed exit — the same instinct as a retry limit or `MaxAutomaticRedirections`.

So: an agent is a `for` loop around the same POST, where your code reads `tool_calls` off the response and appends the results before going round again. `AgentLoop.RunAsync` is 30 lines. There is no other secret.

Longer walkthrough, with a worked example: [question 003](questions/003-why-messages-grow.md).

## LangChain is a library that builds this body

Everything above is roughly 80 lines of C#: `ChatRequest.Build`, `GroundedPrompt.Build`, `AgentLoop.RunAsync`, `ToolCatalog`.

LangChain is a Python and JavaScript library that writes those lines for you, plus loaders, splitters, vector store adapters, retrievers, and an agent executor. LlamaIndex is the same idea with the index at the centre. Semantic Kernel is Microsoft's version for .NET. `Microsoft.Extensions.AI` is the .NET interface layer.

They are worth using. They are not a different technology. Whatever you call from them ends up producing a `messages` array and a `tools` array and posting it to a URL. When a tutorial feels like magic, the question that dissolves it is: *what did that put in the request body?*

The practical consequence for you: the design decisions that are hard are not library decisions. Chunk size, what you retrieve, how you grade it, which tools exist, whether a tool can write — none of those change when you switch libraries. That is why [note 5](05-libraries.md) is a mapping table and not a tutorial.

And the honest sentence stays honest. You have built this pipeline. You have not run LangChain. Those are different claims and an interviewer can tell them apart in about two questions.

## What the trace does not show

`trace` prints the request. It cannot show you the response, because no model is configured. So it teaches the half of the system you control and says nothing about the half you do not.

The model's behaviour given that body — whether it uses passage 1 or the distractor, whether it cites, whether it invents a region — is lab 6. Until then, be precise: you can explain and show exactly what an LLM application sends. You have not yet watched one answer.

## Say this out loud

"An LLM application is a POST with a messages array. RAG means my retrieved passages are a string inside the user message, so if retrieval misses, the fact is not in the request at all. An agent adds a tools array and a loop: the model names a function, my C# runs it, and I append the result as a message with the matching tool_call_id before I call again. The server keeps no state, so every turn resends the whole conversation, which is why the loop has a step limit. LangChain builds that same body in Python. I can print mine with a trace command."

## Check yourself

Run both traces, then answer in `my-answers/` without looking:

1. Open the `trace ask` output. Which exact JSON field holds the runbook text, and what does that tell you about why a retrieval miss cannot be fixed by editing the system prompt?
2. In `trace agent`, the messages count goes 2, 4, 6. Name the two messages added each round and say which field links them.
3. The `tools` array is identical in all three requests. Why is it resent every time, and what does that cost you?
4. A colleague says "we should use LangChain, it does RAG for us." Using the trace, say in two sentences what LangChain would and would not decide for you.
