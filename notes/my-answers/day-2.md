# Day 2 — RAG: finding the right pages

Note: [03-rag.md](../03-rag.md) · Deep version: [code/day-2-retrieval.md](../code/day-2-retrieval.md)
Commands run: `eval`, `ask "Which region is paired with eastus for failover?"`
Date: 2026-10-22

**These answers were given to me, not worked out by me.** Self-test at the bottom.

---

## Question 1

> What does `Recall@3: 8/8` mean — and what does it **not** tell you?

### What it means

There are **8 test questions** in `content/eval/questions.json`. A human wrote each one together with the file that *should* answer it.

For all 8, the expected **file** appeared somewhere in the top 3 search results.

| Part | Meaning |
| --- | --- |
| **recall** | "did we find it **at all**?" |
| **@3** | "we only looked at the **top 3**" |
| **8/8** | 8 of the 8 test cases passed |
| **1.00** | 8 ÷ 8 = 100% |

The right file does **not** have to be first. It only has to be present. That is what *recall* means.

Also: **no model is called.** This measures the search half of RAG only, which is why it runs in about a second, costs nothing, and gives the same answer every time.

### What it does NOT tell me — seven things

**1. Which *section* of the file came back.**

The comparison in `Evaluation.cs` is on the **file name**:

```csharp
hit.Chunk.Source.Equals(question.ExpectedSource)   // "trials.md" == "trials.md"
```

`trials.md` has three sections: *Trial duration*, *Trial storage*, *Leaving a trial*. If search returned only "Leaving a trial" — the wrong section, with no answer in it — eval would still print **PASS**. It is file-level, not chunk-level.

**2. Whether useless passages came along.**
`incidents.md` appears in every failover answer. Score stays 1.00.

**3. Whether something *dangerous* came along.**
On the question *"What severity is a customer-wide outage?"*, position 2 is **`untrusted-excerpt.md`** — the prompt-injection page. It goes into the prompt. eval says PASS and never mentions it. **A retrieval metric is blind to security.**

**4. Whether it was 1st or 3rd.**
Position 1 and position 3 score identically, so I cannot see quality slowly degrading. The metric that would show this is **MRR**.

**5. Anything about the answer.**
No model runs, so it cannot know whether the model read the passage, answered correctly, invented something, or cited the right file.

**6. Anything about question number 9.**
There are only 8 questions. They are not even my questions — they were written when the project was built.

**7. Whether my hybrid search is earning its place.**
I scored the halves separately: **BM25 alone also gets 8/8. The vector search alone also gets 8/8.** So the eval cannot justify running both. That is a statement about my questions being too easy, not about my search being excellent.

---

## Question 2

> Look at the 4 passages from `ask`. One of them does not answer the question. Which one, and why was it returned?

### The answer

**Passage 3: `incidents.md — Severity`.**

```
1. failover.md — Regional failover   (rrf 0.0328)   <- the actual answer
2. failover.md — DNS TTL             (rrf 0.0320)
3. incidents.md — Severity           (rrf 0.0315)   <- this one
4. regions.md — Allowed regions      (rrf 0.0313)
```

It is about Sev1 and Sev2 incident levels. The question was about which region pairs with eastus. It is completely unrelated.

### Why it was returned — measured, not guessed

I scored the two searches separately:

| Search | Where it ranked `incidents.md` | Score |
| --- | --- | --- |
| BM25 (words) | **5th** — correctly unimpressed | 2.5996 |
| Vector (meaning) | **2nd** | 0.3046 |

**The vector search put it there.** Then RRF combined 5th place + 2nd place = 0.0315, which was enough to beat `regions.md`.

### Why the vector search made that mistake

Our embedder is `HashingEmbedder` — a **fake**. It is not a trained model. It hashes each word into one of 384 buckets and adds ±1.

So it cannot tell that "severity levels" and "regions" are different topics. It only sees overlapping common words, and `incidents.md` happens to mention `billing-api` and share ordinary vocabulary.

### What I would fix

**Not the prompt.** The prompt cannot remove a passage that retrieval already chose.

Two real fixes:
1. **A real embedding model** instead of the hashing one.
2. **Reranking** — a second, slower, more accurate pass over the top ~20 that re-scores each passage against the query and keeps the best 4. This is the standard fix and we do not have it.

### One honest refinement

Strictly, **only passage 1 actually answers the question.**

- Passage 2 (DNS TTL) is about timing after a failover, not which region pairs
- Passage 4 (Allowed regions) lists eastus, westus, westeurope but never says which is *paired* with which

`incidents.md` is the most clearly **unrelated**. The other two are topically adjacent but still do not answer it. So three of my four passages are costing tokens without earning their place.

---

## Question 3

> Why does the app use keyword search **and** vector search, instead of just one?

### The theory

Each one is blind where the other can see.

| | BM25 (words) | Vector (meaning) |
| --- | --- | --- |
| "DNS TTL", `Standard_LRS`, "90 days" | **strong** | weak |
| "failover partner" vs "paired region" | weak | **strong** |
| Exact codes and identifiers | **strong** | weak |
| Different words, same meaning | **blind** | **strong** |

BM25 compares **literal strings**. I proved it: asked whether the chunk contains `partner`, it answers no — it cannot see that `partner` means `paired`.

Vector search compares **direction in 384-dimensional space**, so similar meanings land close together even with no shared words.

Running both and fusing the two rankings means one can rescue the other.

### But here is what I actually measured in MY app

I scored both halves alone against 5 paraphrased questions — same meaning, different words:

```
                                                      BM25   VECTOR  HYBRID
What is the failover partner for eastus?              PASS   PASS    PASS
How long does a free evaluation account stay open?    PASS   FAIL    PASS
How quickly do name servers refresh after a cutover?  PASS   PASS    PASS
Where are credentials kept?                           FAIL   FAIL    FAIL
Which locations may I deploy a new customer to?       PASS   FAIL    FAIL

TOTAL                                                 4/5    2/5     3/5
```

**My hybrid search is worse than BM25 alone.** 3/5 versus 4/5.

And there is **no question in my whole project where the vector search beat BM25.** Not one.

### Why

Because my embedder is fake. It counts words via a hash; it cannot see that "locations" means "regions". So it contributes noise, and RRF faithfully mixes that noise in and pushes a correct answer out of the top 3.

> The theoretical case for hybrid is real. It just requires a **real** embedding model. With a hashing embedder, hybrid is a net loss.

*(Caveat: 5 questions is a small sample, and I wrote them. Suggestive, not proven.)*

### So the honest answer

> "The app uses both because keyword search and vector search fail in different ways — BM25 misses paraphrase, embeddings miss exact identifiers. That is why hybrid is standard in production.
>
> But in **my** project I measured it, and the hybrid is actually worse than BM25 alone, because my embedder is a hashing stand-in rather than a trained model. I would not claim my hybrid search is better until I swap in a real embedder and re-run that comparison."

That is a far stronger thing to say than repeating the textbook reason.

---

## What I take from Day 2

- Retrieval sets the ceiling. If search misses, the fact is not in the request and the model cannot save me.
- `eval` is a **regression** test for search. It protects what works; it cannot show how good I could be.
- A score of 1.00 can hide an irrelevant passage, a wrong section, and a prompt-injection page.
- Fusion by **rank** (RRF) exists because BM25 and cosine scores are on incompatible scales.
- **Measure before claiming.** I assumed hybrid was better. I measured, and it is not — in this app, with this embedder.

## Self-test before Day 3

Close this file and answer out loud:

1. What exactly does eval compare — and what does it ignore?
2. Which half of the search put `incidents.md` in the results? What is your evidence?
3. Is your hybrid search better than BM25 alone? What is your number?
