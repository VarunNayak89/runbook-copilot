# Day 3 — Chunking and evaluation

Concept note: [03-rag.md](../03-rag.md), section "Chunk size is a trade"
Code: [`Chunking.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Chunking.cs), [`Evaluation.cs`](../../projects/RunbookCopilot/src/RunbookCopilot/Evaluation.cs)

---

## Technology used

| Thing | What it is | Where |
| --- | --- | --- |
| Splitting strategy | structure-aware (markdown headings) + fixed-size sliding window | `MarkdownChunker` |
| Overlap | 150-char tail repeated into the next window | `MarkdownChunker.Windows` |
| Lazy generation | `IEnumerable<string>` + `yield return` | `Windows` |
| Metric | **recall@k** | `EvalReport.Recall` |
| Grader | **code-based**, exact source-file match | `RetrievalEval.RunAsync` |
| Test integration | xUnit fact + process exit code | `CopilotTests`, `Program.EvalAsync` |

---

## `MarkdownChunker` — two-stage splitting

```csharp
public MarkdownChunker(int maxChars = 1000, int overlapChars = 150)
```

### Stage 1 — split on headings

```csharp
foreach (var line in lines)
{
    if (IsHeading(line))
    {
        Emit();                                 // close the previous section
        heading = line.TrimStart('#').Trim();
    }
    else body.AppendLine(line);
}
Emit();                                         // the final section
```

This is **structure-aware** chunking: a heading usually marks one topic, so sentences under it belong together. For markdown it beats fixed-size splitting, because fixed-size cuts through the middle of ideas.

`IsHeading` requires the trailing space:

```csharp
line.StartsWith("# ") || line.StartsWith("## ") || line.StartsWith("### ")
```

So `# Title` is a heading and `#hashtag` is not. It stops at `###` because deeper levels tend to produce chunks too small to carry context.

The trailing `Emit()` after the loop is the classic off-by-one here. Without it the last section of every file silently disappears.

### The heading is written back into the text

```csharp
var combined = $"# {title}\n{text}";
```

Both indexes then see it. "DNS TTL" exists only in the heading of that section — drop it and the section becomes unfindable by its own name.

### Stage 2 — sliding window with overlap

```csharp
private IEnumerable<string> Windows(string combined)
{
    if (combined.Length <= _maxChars) { yield return combined; yield break; }

    var start = 0;
    while (start < combined.Length)
    {
        var length = Math.Min(_maxChars, combined.Length - start);
        yield return combined.Substring(start, length);
        if (start + length >= combined.Length) yield break;
        start += _maxChars - _overlapChars;        // <- advance by LESS than a window
    }
}
```

With 1000/150:

```
window 1:    0 .. 1000
window 2:  850 .. 1850      <- steps back 150
window 3: 1700 .. 2700
```

**Why overlap exists.** A fact landing exactly on a boundary:

```
...the DNS TTL is | 60 seconds...
```

Window 1 ends "the DNS TTL is". Window 2 starts "60 seconds". Neither can answer the question. The overlap guarantees the complete sentence survives in at least one window. You waste ~15% of space to avoid losing facts.

**The invariant that matters:** `overlapChars < maxChars`, enforced in the constructor. If they were equal, `start` would never advance — an infinite loop producing identical chunks until memory ran out.

`yield return` makes this lazy: windows are produced on demand, not materialised into a list.

### Chunk identity

```csharp
new Chunk($"{source}#{index}", source, title, window.Trim())
```

`failover.md#0`, `failover.md#1`… The id is what lets RRF recognise the same chunk from two rankers. The `source` is what lets an answer cite a file.

---

## The chunk-size trade-off, concretely

| | Small chunks (200) | Large chunks (2000) |
| --- | --- | --- |
| Match precision | high — focused | low — diluted |
| Context kept | **poor** — qualifiers get separated | good |
| Tokens per prompt | low | high |
| Chunks per corpus | many | few |
| Typical failure | *"rotate every 90 days"* split from *"unless it is a trial"* | the right line buried in noise |

There is no correct number. **You choose it by running the eval.** That is Lab 3: set `maxChars: 200`, run `eval`, see which question moves, put it back.

---

## `RetrievalEval` — the grader

```csharp
foreach (var question in questions)
{
    var hits = await _search.SearchAsync(question.Question, topK, cancellationToken);
    var pass = hits.Any(hit => hit.Chunk.Source.Equals(question.ExpectedSource, StringComparison.OrdinalIgnoreCase));
    var top  = hits.FirstOrDefault();
    results.Add(new EvalCaseResult(question.Question, question.ExpectedSource, pass, top?.Chunk.Source ?? "(none)", top?.Score ?? 0));
}
```

### `.Any()` is the definition of recall

`.Any()`, not `hits[0] ==`. The expected file must be **present in the top k**, not necessarily first. That is what "recall" means: *did we find it at all*.

If you asserted on position 1 you would be measuring **precision@1**, a stricter and different metric. Since we send 4 passages to the model anyway, presence is the thing that matters.

### Why `topK` is a parameter

```csharp
await new RetrievalEval(knowledge.Search).RunAsync(questions, topK: 3, CancellationToken.None);
```

`eval` scores at 3. `ask` retrieves 4. Scoring stricter than you serve gives you headroom.

### What the metric is blind to

| Measured | Not measured |
| --- | --- |
| Was the right file in the top 3? | Were the other 2 useful? |
| | Did the model use the passage? |
| | Was the answer correct? |
| | Was it the right *chunk* of the right file? |

`8/8 = 1.00` and a completely wrong product are entirely compatible. We know this: `incidents.md` rides along at position 3 on every failover question, and recall never notices.

### The grader type

This is a **code-based grader** — deterministic, exact comparison. From the [production note](../reference/production-genai-systems.md):

- code-based: fast, cheap, reproducible, rigid ← this
- model-based: flexible, inconsistent ← `AnswerRubric` gestures at it
- human: accurate, expensive

Our suite is a **regression** suite, not a capability suite: all 8 pass, so it protects what works. It contains no deliberately hard questions, so it cannot tell us how good we could be. Naming that gap is worth more than the 1.00.

---

## Wiring the eval into the build

Two integration points, and both matter.

**Exit code**, in `Program.EvalAsync`:

```csharp
return report.Recall == 1 ? 0 : 2;
```

A pipeline can block a deploy on a retrieval regression.

**xUnit fact**, in `CopilotTests`:

```csharp
[Fact]
public async Task Retrieval_eval_recalls_every_runbook()
{
    ...
    Assert.Equal(1, report.Recall);
}
```

Runs in ~100 ms with `dotnet test`. This is the whole idea: **retrieval quality is a unit test**, because the hashing embedder is deterministic and no model is called.

The moment you swap in a real embedding model, this test becomes a network call — slower, costly, and no longer perfectly repeatable. That is a real reason to keep a deterministic embedder for CI.

---

## `AnswerRubric` — the other kind of eval

```csharp
public static IReadOnlyList<string> Check(string answer, string citationFile, string requiredPhrase)
```

Two string checks: is the file cited, is the fact present. Crude, but it catches the two most common failures — a missing citation, and a dropped number.

What it cannot catch: a fluent, correctly-cited sentence that is subtly wrong. Our model turned *"do not fail over **while** a lock is held"* into *"a lock **is** in place"* — every string check passes.

Grading generated text properly is an open problem. Anyone who says otherwise is selling something.

---

## What you would use in production

| This project | Production |
| --- | --- |
| character count (`maxChars`) | **token** count (`tiktoken` / `SharpToken`) — the model's limit is tokens |
| heading split | heading split **plus** semantic/recursive splitting for non-markdown |
| 150-char overlap | 10–20% of chunk size, tuned by eval |
| chunk at startup | chunk on ingest, store with vectors |
| recall@3 on 8 questions | recall@k, **MRR**, **nDCG** on 50–200 questions |
| code grader only | code + model-based + periodic human review |
| regression suite only | regression **and** capability suites |
| single run per question | `pass@k` / `pass^k` over repeated runs |

**`nDCG`** and **`MRR`** are the names to know. MRR (mean reciprocal rank) rewards the right answer being *higher*, which recall@3 ignores entirely. If your distractor problem matters, MRR is the metric that would show it.

---

## Interview questions on this code

1. Why `.Any()` and not `hits[0]`? What metric would the other give?
2. What breaks if `overlapChars >= maxChars`?
3. Why is the heading copied into the chunk text?
4. Your eval is 8/8. What does that not tell you?
5. Why does `eval` return exit code 2?
6. Why is character count the wrong unit, and what is right?
7. How would you measure that the right chunk ranks *higher*, not just that it is present?

---

## Try it — Lab 3

```powershell
# in KnowledgeBase.cs, change:  new MarkdownChunker()  ->  new MarkdownChunker(maxChars: 200)
dotnet run --project src/RunbookCopilot -- eval
```

Which question moved? Was the fact cut in half, or just not found? Put it back afterwards.
