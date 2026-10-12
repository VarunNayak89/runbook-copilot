# Labs

Do these in order. After each one, write a few sentences in `notes/my-answers/` saying what you changed and what the command printed. That writing is the evidence you understand it.

## 1. Baseline

Run `dotnet test` and `dotnet run --project src/RunbookCopilot -- eval`.

Done when you can say the recall number and what it does not measure.

## 1b. Read what actually gets sent

Run both traces and read the JSON, not the prose around it.

```powershell
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
dotnet run --project src/RunbookCopilot -- trace agent "Is billing-api healthy and where is failover documented?"
```

In `trace agent`, count the messages in each of the three requests and find the `tool_call_id` that links a `tool` message back to the `assistant` message that asked for it.

Done when you can point at the field holding the retrieved passages, and say why the whole conversation is resent on every turn. The note for this lab is [08-the-request-body.md](../../notes/08-the-request-body.md).

## 2. Add a document

Add `content/runbooks/backups.md` with one specific fact that appears in no other file (a number and a noun). Add a question to `content/eval/questions.json` whose expected source is `backups.md`.

Done when `eval` is still 100% and you can point at the chunk that matched.

## 3. Break chunking on purpose

In `KnowledgeBase.cs`, construct `MarkdownChunker` with a small `maxChars` (try 200). Run `eval`. Put the constructor back when you are finished.

Done when you can name a question that moved, and whether the right fact was cut in half.

## 4. Add a tool

Add `list_services` to `ToolCatalog`. It takes no arguments and returns every service id and status from `content/services.json`. Extend `ScriptedChatClient` only if you want the stand-in to call it. Add a test.

Done when `dotnet test` passes and you can say why the tool result goes back to the model as a `tool` message instead of being printed only to the console.

## 5. Read the injection page

Open `content/runbooks/untrusted-excerpt.md` and the system text in `GroundedPrompt.cs`.

Done when you can explain why a sentence inside a retrieved page is data, and why the system sentence is a mitigation rather than a guarantee. Do not delete the mitigation.

## 6. Connect a model

Ollama and `llama3.2` are installed. Start the server if it is not running, then:

```powershell
$env:COPILOT_PROVIDER = "OpenAICompatible"
$env:COPILOT_ENDPOINT = "http://localhost:11434/v1"
$env:COPILOT_CHAT_MODEL = "llama3.2"
dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
```

The first pass of this lab is already run and recorded in [my-answers/lab-6-transcript.md](../../notes/my-answers/lab-6-transcript.md), including three things the model got wrong. Re-run the commands yourself — a transcript you only read is not an experience you had — then write your own answers in `notes/my-answers/lab-6.md`.

Done when you can describe one grounded sentence and one place the model added something the passages did not say.

## 7. Make the extractor catch the substitution

Lab 6 found that `extract "...in mars..."` returns `westeurope` and is accepted, because an allowlist cannot tell a correct answer from a well-formed invented one.

Add a check that each extracted value is supported by the input sentence, and decide what the app does when it is not: reject, or flag for a human. Add a test with `mars` that fails before your change.

Done when `extract "Create a tenant for Fabrikam in mars, environment prod"` no longer exits 0, and you can say why you put the check where you put it.

## 8. Port something else to LangChain

[projects/langchain-port](../../projects/langchain-port/README.md) covers `ask`. `eval` came with it. Neither `agent` nor `extract` is ported.

Port `extract`: a prompt template, a structured output parser, and the same validation. Then decide whether the library's parser made the lab 7 problem better or just differently shaped.

Done when you can say which part LangChain genuinely saved you and which part you still had to design.
