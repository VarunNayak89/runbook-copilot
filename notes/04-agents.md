# Agents

An agent is a loop. The model may answer, or it may ask your program to run a tool, read the result, and decide again. The tools are ordinary C#. The model does not gain permissions you did not code.

## What a tool call looks like

You send a list of functions: name, description, and a JSON schema for the arguments. `ToolCatalog.Definitions` has three:

- `search_runbooks` — the same hybrid search `ask` uses
- `get_service_status` — reads `content/services.json`
- `get_runbook` — reads one markdown file by name

A model that supports tools responds with `tool_calls` instead of a final answer: an id, a function name, and an arguments string. `OpenAiChatClient` parses that. `AgentLoop` runs the function, appends a message with role `tool` and that same id, and calls the model again.

Run `trace agent "<question>"` to see all three request bodies for one loop. The messages array goes 2, 4, 6. Reading that is faster than reading this paragraph, and [note 8](08-the-request-body.md) walks through it.

The loop stops when the model returns no tool calls, or when `maxSteps` (4) is hit. The step limit is the product. Without it a confused model can call tools until you stop paying.

## The stand-in

`agent --scripted` uses `ScriptedChatClient`. It is not a model. If the question names a service, it calls `get_service_status`, then `search_runbooks`, then echoes the tool text. The tests assert that sequence. The value of the stand-in is that the loop, the tool router, and the path check are real and repeatable with no network.

When you describe this, say "scripted tool loop" until lab 6 replaces the client with `OpenAiChatClient`. The HTTP client is already written. The experience of a model choosing a surprising tool is not, until you have seen it.

## What the real model did instead

Lab 6 ran the same question against llama3.2. It did not follow the stand-in's script.

It called `get_service_status` **and** `get_runbook("failover.md")` in the same turn, in parallel, and never called `search_runbooks` at all. It judged that reading the whole file beat searching it.

Two things to take from that. First, the stand-in taught you the loop but not the behaviour — a scripted client can only do what you scripted. Second, `AgentLoop` already handled parallel calls correctly, because of `foreach (var call in turn.ToolCalls)`. That code had never run with more than one call in the list until a real model exercised it. Code being correct and code being tested are different states.

The answer also went slightly wrong in a way worth memorising: `failover.md` says "Do not start failover while a provision lock is held," and the model reported "a provision lock is in place." It turned a conditional rule into a claim about current state that no tool had checked. Everything else in the answer was sourced, which is what makes that shape of error dangerous. Transcript: [my-answers/lab-6-transcript.md](my-answers/lab-6-transcript.md).

## `ask` is not an agent

`ask` always retrieves, then optionally generates once. The path is fixed. That is sometimes called a chain. It is easier to test, cheaper, and easier to explain. Use it when you know the steps.

Use the agent when the next step depends on what came back. "Is billing-api healthy, and where is failover documented?" needs a status lookup and a document lookup. A single retrieval might answer only half.

Most production "agents" that work are a short loop with a few tools and a step limit. A general computer-using agent is a different, much riskier product. Do not describe this app as that.

## Tools are an API

`get_runbook` rejects `..` and slashes. A model can be talked into requesting `..\something`. The tool has to refuse, the same way a web handler refuses a path. There is a test for this. The model is an untrusted caller of your functions.

Tool errors are returned as the tool message, not thrown past the loop, so the next turn can recover or admit it does not know. `Unknown service` is a result.

## What you should refuse to automate

A tool that provisions, deletes, or changes DNS is a production incident waiting for a plausible paragraph. The tools in this app are read-only. If you add a write tool later, the pattern is: the model proposes, a human confirms, your existing validation runs, then the write happens. The model does not get the delete verb.

## Say this out loud

"My agent is a loop with a step limit. The model can call search, service status, or read one runbook. I execute the tool in C# and send the result back. The scripted client lets me test that loop without a model. The ask command is a fixed retrieve-then-answer path, and I use that when I already know the steps. The tools are read-only, and the file tool rejects paths."

## Check yourself

1. What is the difference between `ask` and `agent` in this project?
2. Why does a tool result go back into the message list instead of only printing on screen?
3. Why is `maxSteps` part of the design, not just a safety afterthought?
