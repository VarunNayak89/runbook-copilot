# RAG

This note uses short sentences. Read one part. Stop. Then read the next part.

Want the deep technical version with the algorithm names? That is [notes/code/day-2-retrieval.md](code/day-2-retrieval.md). Read this one first.

---

# Part 1: The problem

The model never saw your company documents.

So if you ask:

> "Which region is paired with eastus for failover?"

The model does not know. Northwind Cloud is your private company. It is in no book.

But the model is polite. **It will guess.** The guess will sound correct. That is bad.

---

# Part 2: The idea

Very simple idea:

> **Before you ask the question, give the model the correct pages.**

Think about a new employee. They do not know your rules. So you do this:

1. You go to the manual.
2. You photocopy the 4 correct pages.
3. You put the pages on their desk.
4. Then you ask your question.

Now they read the pages. They do not guess.

**That is RAG. That is the whole idea.**

## What the letters mean

RAG = **R**etrieval **A**ugmented **G**eneration.

| Letter | Word | Simple meaning |
| --- | --- | --- |
| R | Retrieval | Find the correct pages |
| A | Augmented | Add the pages to the question |
| G | Generation | The model writes the answer |

## Two halves, and you must separate them

This matters a lot:

| Half | What it does | Can you test it without a model? |
| --- | --- | --- |
| **Retrieval** (R and A) | finds the pages | **Yes.** `eval` does this. |
| **Generation** (G) | writes the sentence | No. You need a model. |

Most people say "RAG" and mean the whole thing. **Keep them separate in your head.**

Why? Because you can fix and measure retrieval for free, with no API key and no cost. Do that first.

---

# Part 3: The 5 steps

Here is what happens when you run `ask`.

## Step 1: Cut the documents into pieces

We have 7 markdown files. We cut them into **16 small pieces**.

One piece is called a **chunk**.

Why cut them? Two reasons:

- We cannot send 7 whole files every time. Too big, too slow, too expensive.
- Small pieces are easier to search. One chunk is about one topic.

We cut at every heading (`#`, `##`, `###`), because a heading usually starts a new topic.

*Code: `Chunking.cs`*

## Step 2: Turn each chunk into numbers

Each chunk becomes a list of **384 numbers**.

Why numbers? Because then "find similar text" becomes maths. Computers are good at maths.

Two pieces of text with similar meaning get numbers pointing in a similar direction.

This list of numbers has a name: an **embedding**, or a **vector**. Same thing.

> **Be honest about ours.** Our embedder is fake. It is not a trained model. It just counts words in a clever way. It is here so the app runs offline with no download. A real embedder is better and costs money.

*Code: `Embeddings.cs`*

## Step 3: Keep the numbers in a list

We store all 16 vectors in memory, in a `List`.

People call this a **vector database**. Ours is not a database. It is a list.

For 16 chunks a list is fine. For 10 million chunks you need a real product (Azure AI Search, pgvector, Qdrant). Those are the same idea, but fast and saved to disk.

*Code: `Search.cs`, class `VectorIndex`*

## Step 4: Search, two different ways

Now the user asks a question. We search **twice**.

| Search | What it matches | Name of the method |
| --- | --- | --- |
| Search 1 | the **words** | BM25 |
| Search 2 | the **meaning** | vector / embedding search |

Then we join the two result lists into one.

Part 4 below explains why we do it twice. This is the most important part of the note.

*Code: `Search.cs`*

## Step 5: Paste the winners into the question

We take the best 4 chunks. We put them in the prompt, above the question.

Then we send it.

*Code: `GroundedPrompt.cs`*

---

# Part 4: Why two searches?

This is the part worth understanding properly.

## The two searches are good at different things

**BM25 matches words.** It is strong when the question uses the same rare words as the page.

- Question: "What is the DNS TTL?"
- Page says: "the DNS TTL is 60 seconds"
- Same rare words. BM25 finds it easily.

But BM25 is **blind to different words with the same meaning**:

- Question: "What is the failover partner for eastus?"
- Page says: "eastus is **paired** with westus"
- No shared words. BM25 scores this low.

**Vector search matches meaning.** It can see that "failover partner" and "paired region" are similar.

But vector search is **weak on exact codes**. A rare identifier like `Standard_LRS` is a blurry signal in a vector.

| | BM25 (words) | Vector (meaning) |
| --- | --- | --- |
| "DNS TTL" | **strong** | weak |
| "failover partner" vs "paired" | weak | **strong** |
| Exact codes and numbers | **strong** | weak |

**Each one is blind where the other can see.** So we use both.

## Real numbers from our app

I ran both searches separately for the question *"Which region is paired with eastus for failover?"*

**BM25 said:**

```
15.7089   failover.md   Regional failover
 4.3583   failover.md   DNS TTL
 3.0119   regions.md    Allowed regions
 2.6463   failover.md   Provision lock
 2.5996   incidents.md  Severity
```

**Vector search said:**

```
0.7770   failover.md    Regional failover
0.3046   incidents.md   Severity          <-- look at this
0.2182   failover.md    DNS TTL
0.1581   trials.md      Trial storage
0.1543   regions.md     Allowed regions
```

Both put the correct chunk first. Good.

## The problem: the scores do not match

Look again:

- BM25 gives **15.7089**
- Vector gives **0.7770**

These are completely different scales. **You cannot add them.** If you tried, BM25 would always win, because its numbers are bigger.

## The solution: forget the scores, use the positions

We throw the scores away. We only use **the position in each list**.

```
1st place  ->  1/61  =  0.0164 points
2nd place  ->  1/62  =  0.0161 points
3rd place  ->  1/63  =  0.0159 points
```

Then we add up the points from both lists.

This method has a name: **Reciprocal Rank Fusion**, or RRF.

## Why the point values are so close

That is on purpose. Watch:

| Chunk | BM25 position | Vector position | Total points |
| --- | --- | --- | --- |
| failover.md / Regional failover | 1st = 0.0164 | 1st = 0.0164 | **0.0328** |
| failover.md / DNS TTL | 2nd = 0.0161 | 3rd = 0.0159 | **0.0320** |
| incidents.md / Severity | 5th = 0.0154 | 2nd = 0.0161 | **0.0315** |

A chunk in 2nd place on **both** lists gets 0.0322.
A chunk in 1st place on **only one** list gets 0.0164.

> **So agreement wins.** If both searches like a chunk, it goes to the top.

That is exactly what you want from two independent opinions.

*(The number 60 comes from a research paper. It is the normal default. Do not change it to fix one bad result.)*

---

# Part 5: What the score means

Run this:

```powershell
dotnet run --project src/RunbookCopilot -- eval
```

It prints:

```
Recall@3: 8/8 = 1.00
```

## What it measures

We have 8 test questions. For each one, we wrote down which file **should** answer it.

**Recall@3** asks one question only:

> Was the correct file somewhere in the top 3 results?

8 out of 8 means yes, every time.

| Word | Meaning |
| --- | --- |
| recall | "did we find it at all?" |
| @3 | "we only looked at the top 3" |

Note: it does not have to be **first**. It only has to be **there**. That is what "recall" means.

## What it does NOT measure

This is the important part. **8/8 does not mean your system is good.**

| It tells you | It does not tell you |
| --- | --- |
| the right file was in the top 3 | whether the other 2 results were useful |
| | whether the model read it |
| | whether the answer was correct |
| | whether the right **chunk** of the file won |

## A real example from our app

Look at the vector search results again. The second result was `incidents.md`.

`incidents.md` is about **severity levels**. The question was about **regions**. It answers nothing.

But it still appears as passage 3 in every answer. It costs tokens. It can confuse the model.

**And recall@3 still says 1.00.** The score cannot see this problem.

> **If you only say "my score is 8 out of 8", you have over-claimed.**
> Say the score, then say what it misses. That is the honest version, and it is more impressive.

## Which search caused it?

We can tell, because we measured them separately:

- BM25 ranked `incidents.md` **5th** — correctly unimpressed
- Vector search ranked it **2nd**

**So our weak fake embedder caused it.**

That is a real diagnosis, not a guess. The fix is a better embedder, or a second ranking step called **reranking**. The fix is **not** a better prompt.

---

# Part 6: How big should a chunk be?

There is no correct answer. It is a trade.

| | Small chunks | Big chunks |
| --- | --- | --- |
| Match quality | focused, precise | diluted by extra text |
| Keeps context? | **no** — can cut a fact in half | yes |
| Cost per question | low | high |

## Why small chunks are dangerous

Imagine the document says:

> Rotate the key every 90 days.
> This does not apply to trial tenants.

If your chunks are too small, these become **two separate chunks**.

Now search returns only the first one. The model answers "every 90 days". **It is wrong**, and it looks completely confident.

## How to choose

**You do not guess. You measure.**

Change the size, run `eval`, look at the score. That is Lab 3.

Production teams pick a chunk size by running their eval set. Not by copying a blog post.

---

# Part 7: The 5 ways RAG fails

Learn this list. Interviewers ask it.

### 1. The right page was not found

The fact is simply not in the request. **The model cannot save you.**

Fix the chunking or the search. Then run `eval` again. Do not touch the prompt.

### 2. The right page was found, but a wrong page came too

This is our `incidents.md` problem. The extra page costs money and can mislead.

Fix: a better embedder, or add reranking.

### 3. The documents are old

Search will confidently return last year's policy.

Something must rebuild the index when a document changes. Our app rebuilds everything at startup. Fine for 7 files. Wrong for a big system.

### 4. Two pages disagree with each other

The model will pick one, and sound certain about it.

You need one source of truth. A better prompt will not fix a contradiction in your data.

### 5. The answer is in no document

The correct behaviour is **"I do not know."**

Test this on purpose. A system that always gives an answer cannot be trusted.

---

# Say this out loud

> "RAG for me is: cut the documents into chunks, turn them into vectors, also index the words with BM25, then combine the two rankings by position using RRF. I put the best passages in the prompt and ask for a citation.
>
> I measure retrieval with recall@3. It is 8 out of 8 on my set. But that score only says the right file was present. It does not grade the sentence the model writes, and it does not notice that one irrelevant passage rides along every time.
>
> I know which half caused that: BM25 ranked it 5th, my embedder ranked it 2nd. My embedder is a hashing embedder, not a trained one, so the app runs offline. A real embedding model plugs into the same interface."

---

# Check yourself

Write your answers in `my-answers/day-2.md`. Two or three sentences each.

1. What does `Recall@3: 8/8` **not** tell you? Use `incidents.md` as your example.
2. Why does this app keep BM25 when it already has vector search? Give one question where BM25 wins and one where the vector wins.
3. BM25 scored 15.7089 and the vector scored 0.7770. Why can we not just add those two numbers?
4. A new question fails `eval`. What do you change first: the prompt, or the chunks? Why?
