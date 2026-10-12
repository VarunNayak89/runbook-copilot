# How an LLM works

You do not need to derive the math. You do need a model of the machine that matches what your code is doing.

## Prediction, not a database

A large language model is a function from tokens to the next token. It was trained on a huge amount of text to be good at that guess. At runtime you send tokens in and read tokens out. The weights (the learned numbers, also called parameters) stay fixed. `ask` does not train anything. Fine-tuning would change weights. You are not doing that, and for "answer from these runbooks" you should not start there.

The model has no live connection to `content/runbooks` until your code puts that text in the request or behind a tool. If the answer is not in the prompt and not in a tool result, a fluent answer is still only a plausible continuation. That is the whole reason RAG exists.

## Tokens and the context window

A token is not a word. Common words are one token. Rare words and identifiers split into several. A rough English budget is one token per four characters. The context window is the maximum tokens the request may contain: system message, passages, question, and the answer still to be generated.

If you paste an entire wiki into the prompt, you pay for every token, you add latency, and the model attends to irrelevant pages. Chunking and retrieval exist to spend the window on a few relevant passages. In this app that budget is the four passages `ask` retrieves, not the whole `content/runbooks` folder.

## Temperature

`OpenAiChatClient` sends `temperature` 0.1. Temperature scales how willing the model is to pick a less likely next token. Low temperature is what you want when the task is "say what the runbook says." High temperature is for varied wording, and it also varies the facts. For extraction and grounded answers, keep it low and still validate.

## Attention, in one paragraph

Inside the model, each token can weight the other tokens in the window. That mechanism is called attention. It is why a passage at the top of the prompt can affect a word at the end, and also why a distractor passage can pull the answer off course. You are not implementing attention. You are deciding which tokens get into the window.

## Hallucination

Hallucination means the text is grammatical and wrong. It is a normal outcome of next-token prediction, not a rare bug you can prompt away completely. The controls you have in this app are:

- retrieve first, so the relevant fact is in the window
- tell the model to say it does not know (`GroundedPrompt.cs`)
- print the passages so a human can check
- validate JSON against an allowlist (`TenantRules`)
- measure retrieval with `eval` before you admire the prose

A citation like `[failover.md]` is not proof. The sentence next to it still has to be in that file. Lab 6 is where you practice catching that.

## What people mix up

Training is how the weights were produced. Inference is the call you make. RAG does not retrain the model. An embedding model is a different call from a chat model: it returns a vector, not a sentence. `HashingEmbedder` is a stand-in for that second call. It is deterministic and weak. A neural embedding model is better at paraphrase ("paired region" versus "failover partner") and is configured with `COPILOT_EMBEDDING_MODEL` when you have one.

## Say this out loud

"I don't train the model. I call it. It only knows what I put in the prompt or what a tool returns. I keep temperature low, I retrieve a few passages instead of pasting the whole corpus, and I still check the answer against those passages because a fluent sentence can be wrong."

## Check yourself

1. Why does pasting every runbook into the prompt get worse as the corpus grows, even if the context window can hold it?
2. What is the difference between the chat call and the embedding call?
3. Name two controls in this repo that exist because the model can be fluent and wrong.
