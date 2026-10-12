# RAG

RAG is retrieval-augmented generation. Retrieve passages from your own documents, put them in the prompt, then generate. The model is not retrained on the runbooks.

People say "RAG" and mean the whole product. Be able to separate the two halves. Retrieval can be measured with no model. Generation cannot. `eval` is the retrieval half. `ask` with a configured chat model is the generation half.

## The pipeline in this app

1. **Load.** `KnowledgeBase` reads `content/runbooks/*.md`.
2. **Chunk.** `MarkdownChunker` splits on `#` headings. A section longer than 1000 characters is windowed with 150 characters of overlap so a fact on the boundary is not stranded in one window.
3. **Embed.** `HashingEmbedder` turns each chunk into 384 numbers. Each word adds or subtracts 1 in a bucket chosen by a stable hash. The vector is not from a neural model. Swap it by setting `COPILOT_EMBEDDING_MODEL`; `IEmbedder` is the seam.
4. **Index.** `VectorIndex` keeps those vectors in memory. `LexicalIndex` keeps BM25 statistics: how often a word appears in a chunk, and how rare it is across chunks.
5. **Retrieve.** The question is embedded and also scored with BM25. `HybridSearch.Fuse` combines the two ranked lists with reciprocal rank fusion: each list adds `1 / (60 + rank)`.
6. **Prompt.** `GroundedPrompt` pastes the top passages and the question.
7. **Generate.** Only if a chat model is configured. Otherwise `ask` prints the passages and stops.
8. **Check.** You read the passages. Later, `AnswerRubric` checks that an answer contains a citation and a required phrase.

At this corpus size the index is a list. A vector database (Azure AI Search, pgvector, Qdrant) is the same search plus filters and scale. The idea to learn is the search, not the product name.

## Why hybrid

BM25 is strong when the question shares rare words with the page: "DNS TTL", "Standard_LRS", "90 days". It misses paraphrase.

A neural embedding is strong at paraphrase and can miss exact identifiers, because the identifier is rare in its training and the vector is a blur.

Hybrid search runs both and fuses ranks. The constant 60 in RRF is the usual default, not a number you tune on eight questions and then trust.

The hashing embedder behaves more like a bag of words than like a neural embedding. It is here so the pipeline runs with no download and so you can see the interface. When you say "embeddings" in an interview, say which one: hashing for the offline path, and a named model if you have configured one.

## What recall@3 is

`content/eval/questions.json` has eight questions and the file that should answer each one. Recall@3 is the fraction of questions where that file appears anywhere in the top 3 chunks.

A score of 8/8 means the right file was present. It does not mean every passage was relevant. On the failover question, `failover.md` is first and `incidents.md` can still appear lower, because common words and a coarse vector pull neighbors in. Generation has to ignore that neighbor. If you only quote the 1.00, you have over-claimed.

Recall also says nothing about the sentence the model writes. A perfect retrieval score with a fabricated answer is a failed product. That second measurement is `AnswerRubric`, and it needs a model, which is lab 6.

## Chunk size is a trade

Short chunks point at the exact heading and can cut a fact off from the sentence that qualifies it. Long chunks keep the qualifier and also keep unrelated sentences, so the match is diluted and the prompt is more expensive.

Lab 3 asks you to shrink `maxChars` and watch `eval`. The lesson is the movement in the score, not a universal best size. Production systems pick a size by running the eval set, not by copying a blog.

## Failure modes worth naming

- The fact is in a file the retriever did not return. Generation cannot save you. Fix the chunking or the query, then re-run `eval`.
- The right file is returned along with a distractor. The prompt and the human review have to handle it.
- The corpus is stale. Retrieval will confidently return the old page. Something has to rebuild the index when the markdown changes. This app rebuilds in memory on every process start, which is fine for seven files and wrong for a large changing corpus.
- Two chunks disagree. The model will pick one fluently. You need a source of truth, not a better adjective in the prompt.
- The question is not in the documents. The correct behavior is "I do not know." Test that on purpose. A system that always answers is untrustworthy.

## Say this out loud

"RAG for me is: chunk the runbooks, embed them, also index the words with BM25, fuse the two rankings, put the top passages in the prompt, and require a citation. I measure retrieval with recall@3, which is 8 out of 8 on my set, and I know that score does not grade the generated sentence. The default embedder is a hashing embedder so the app runs offline. A neural embedding plugs into the same interface."

## Check yourself

1. What does a recall@3 of 1.00 not tell you? Use the failover question as the example.
2. Why does this app keep BM25 when it already has vectors?
3. What would you change first if a new question failed `eval`: the prompt wording, or the chunks? Why?
