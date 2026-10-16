# Your questions, answered

Every question you ask me gets written here as its own file. Nothing is lost.

These are **your** questions, from real confusion while reading the code. That makes them more useful than a tutorial, because a tutorial answers the questions the author imagined, not the ones you actually had.

## The list

| # | Question | Short answer |
| --- | --- | --- |
| [001](001-why-post-not-get.md) | We get an answer back from the LLM. Why is it POST and not GET? | Because the answer does not exist yet. You are asking the server to create one. |
| [002](002-what-can-i-claim.md) | Can I say in an interview that I built a RAG system? | Not after Day 1. And never connect it to your employer's product. The honest version of the story is stronger — read this one before any interview. |
| [003](003-why-messages-grow.md) | Why does the message count grow 2 → 4 → 6? And what is `maxSteps`? | The server forgets everything, so you resend the whole conversation every turn. Each round adds a pair: the model's request, then your answer. `maxSteps` exists because the *model* decides when your loop ends. |
| [004](004-how-the-model-knows-the-functions.md) | How does the model know which functions exist? Does it get all of them? | Yes — all of them, every turn. Your code hands over the whole menu; the model picks a dish; your code is the kitchen. RAG sends no functions at all. |
| [005](005-how-retrieval-actually-works.md) | How does `trace ask` actually find the right passage? Vector database or embeddings? | Fake offline embeddings, a `List<>` not a database, plus BM25 — fused by rank. Walks the real scores through all four files, and shows which half caused the distractor. |
| [006](006-bm25-vs-vector-and-what-384-means.md) | Does BM25 also do embedding? What does 384 mean? | BM25 does **no** embedding — it stores a `Dictionary<string,int>` and breaks the question into words. Vector search embeds the whole question. 384 = dimensions, fixed by the model, and it must match between question and chunks. |

## How to use this folder

Read a file again a few days later. If the answer feels obvious now, you have learned it. If it still feels new, read it once more.

Before an interview, read only the **Short answer** column above. If you can expand each one into three sentences without opening the file, you are ready.

## Ask more

There is no stupid question. A question you do not ask becomes a gap you cannot explain later.

When something is unclear, say so and I will add it here.
