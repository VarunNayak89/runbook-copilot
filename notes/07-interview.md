# Interview

Speak from the files. If you cannot open the file and point, leave that sentence out.

## Two minutes

"I built a small .NET console app called Runbook Copilot. It answers questions over a set of operations runbooks.

I chunk the markdown by heading. I rank chunks two ways: BM25 for exact words, and a vector from an embedder. I fuse the ranks with reciprocal rank fusion and put the top passages into a prompt that must cite the file and must say it does not know. I measure retrieval with recall at 3. On my eight questions the right file is always in the top 3. I also know a distractor passage can still ride along, so I print the passages next to any answer.

There is a second command that is an agent: a loop that can search the runbooks, read service status, or open one file. The tools are read-only, the file tool rejects paths, and the loop has a step limit. I test that loop with a scripted model so it does not need an API key. The HTTP client for a real OpenAI-compatible or Azure OpenAI endpoint is in the same interface.

I also extract a tenant request into JSON and reject regions that are not on the allowlist. The prompt asks for the allowlist. The C# validator enforces it.

I ported the retrieval path to LangChain to check I was not just learning my own code. Same corpus, same constants, same score, and the two still disagree on which passage lands third."

Then, if they want depth, go to one of the lab 6 stories below. Specific beats impressive, and a mistake you found beats a feature you built.

## Questions they ask, and where the answer lives

| They ask | You open |
| --- | --- |
| How do you stop hallucinations? | `GroundedPrompt.cs`, then say you still verify. You do not "stop" them. You ground and check. |
| How do you evaluate? | `Evaluation.cs`, `content/eval/questions.json`, the recall number from your last `eval` run. |
| Why hybrid search? | `Search.cs`. Rare words versus paraphrase. |
| What is your chunking strategy? | `MarkdownChunker`. Headings, then a window. Mention lab 3 if you did it. |
| Walk me through an agent turn. | `AgentLoop` and one `--scripted` transcript. |
| How do you handle bad model output? | `TenantRules` and the fence stripping in `TenantRequestReader`. |
| Did you use LangChain? | `projects/langchain-port/ask.py` and its README. Say what it did and did not decide for you. |
| What does an LLM app actually send? | `trace ask`. Open the JSON. Note 8. |
| How is this different from fine-tuning? | Note 1. You did not change weights. You retrieved. |
| What would you do with ten million documents? | A hosted index, embed on write, filter before search, the same eval set. |
| What would you not let the agent do? | Provision or delete. Note 6. |

## Stories that are already true

Use these only if you did the thing.

- **The metric.** You ran `eval`, saw 8/8, and can explain that the third passage on the failover question may still be the wrong file.
- **The validator.** A model (or the stub in the test) can wrap JSON in a fence, and the region `mars` is rejected even if the JSON parses.
- **The path check.** `get_runbook` with `..\secrets.md` returns an error string. You wrote or you read the test `Runbook_tool_refuses_a_path`.
- **The chunk experiment.** Only after lab 3. "I set max chars to 200 and question X failed because the fact was split."
- **The silent substitution.** True since lab 6. "I asked it to extract a tenant for region `mars`. The prompt listed the three legal regions. The model returned `westeurope` and my validator accepted it, four times out of four. The allowlist catches malformed output, not confident fabrication. Nothing in the pipeline compared the extracted value back to the input sentence, and that is the check I would add."
- **The surprising tool.** True since lab 6. "My scripted stand-in always called status then search. The real model called status and read the whole runbook file in one turn, in parallel, and never searched. My loop handled it because I iterate the tool call list, but that path had never actually run with two calls in it."
- **The overstated runbook.** True since lab 6. "The runbook says do not fail over while a provision lock is held. The model's answer said a provision lock is in place. It turned a rule into a status claim that no tool had checked, and everything else in the answer was correctly sourced."
- **The port that disagreed.** True since the LangChain port. "I rebuilt the retrieval path in LangChain with the same corpus, the same embedder, and the same RRF constant. Both score 8 of 8 on recall@3, and they still rank passages 3 and 4 in opposite order, because the splitters and the BM25 implementations differ. Two correct builds of one design disagree below the metric."

Do not invent a production outage, a dollar cost, or a LangChain deployment.

## Resume line

Current, and all of it true:

"Built a .NET RAG console app: heading chunking, hybrid BM25 and embedding retrieval with reciprocal rank fusion, recall@3 eval, grounded prompts, and a read-only tool-calling loop against a local Ollama model. Ported the retrieval path to LangChain and compared the two."

What is still not yours to claim: LangGraph, a LangChain agent, a hosted vector database, a production deployment, anything at scale, and any of this as work you shipped at your day job.

## Check yourself

1. Say the two-minute story once, out loud, with the app closed. Where did you stall? That note is the one to reread.
2. What is your recall number from the last `eval` you ran, not from memory of this file?
3. Which resume words are you leaving off until a later lab, and why?
