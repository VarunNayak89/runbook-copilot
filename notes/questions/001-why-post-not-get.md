# 001 — Why is calling an LLM a POST and not a GET?

**Asked on 2026-10-11, while reading `Chat.cs`.**

> In an API, POST means write. For the LLM we get an answer back. So why is it not GET?

This is a good question. It means you already understand REST properly.

---

## Short answer

> We use POST because the question is too big for a URL, because the text is private, and most of all because **the answer does not exist yet**. We are asking the server to create one. `POST /chat/completions` means "create a new completion", the same way `POST /orders` means "create a new order".

---

## First, what POST really means

You learned:

- `GET` = read
- `POST` = write or create

That is the REST convention and it is correct. But POST means a little more than "write to a database":

> **POST = "here is some data. Do something with it. Tell me the result."**

The server might save the data. Or it might only calculate something. Both are POST.

**And a POST always sends data back.** You already use POSTs that return an answer:

| Request | You send | You get back |
| --- | --- | --- |
| `POST /login` | username, password | a token |
| `POST /payments` | card, amount | a receipt |
| `POST /chat/completions` | your question | the answer |

Login is a POST, and it gives you a token back. So "POST returns nothing" was never true.

---

## Reason 1: a GET has no body

A `GET` request has no body. All the data must go inside the URL:

```
GET /chat?question=Which region is paired with eastus...
```

Now look at a real request from this project. Run:

```powershell
dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"
```

The `content` of the user message is more than 1000 characters, because all four runbook passages are inside it. A real production application often sends 50,000 characters.

**A URL cannot be that long.** Most web servers reject a URL longer than about 2000 to 8000 characters.

A POST body has no practical limit. So the passages fit.

---

## Reason 2: URLs end up in log files

This reason matters at work.

A URL is written into many places automatically:

- browser history
- proxy server logs
- web server access logs

If the question is in the URL, then your company's private runbook text is now sitting in log files that many people can read.

A POST body is not logged by default. The private text stays private.

This is the same reason you never put a password in a URL.

---

## Reason 3: you are not reading. You are creating.

This is the real answer.

`GET` means: *give me a thing that already exists.*

```
GET /orders/123        →  Order 123 already exists. Send me a copy.
```

Now ask yourself: when you call the LLM, **does the answer already exist on the server?**

No. There is no answer sitting there waiting for you. Nothing exists until you ask.

The server must **make a brand new answer**, just for you, right now.

Making a new thing = creating = **POST**.

Look at the URL one more time:

```
POST /chat/completions
```

A "completion" is the thing being made. You are saying **"create a new completion"**.

It has exactly the same shape as:

```
POST /orders             →  create a new order
POST /chat/completions   →  create a new completion
```

So POST is correct by the REST rules too. Your rule was right. You only had the wrong idea about **what** was being created. You thought you were reading an answer. You are asking for one to be made.

---

## Reason 4: a GET must be repeatable. The LLM is not.

The HTTP standard says a `GET` should be:

- **the same every time** — same URL, same answer
- **safe to cache**
- **safe to repeat automatically**

The LLM breaks all three:

- the same question can give a different answer
- caching the answer would often be wrong
- every call **costs money**

Browsers, proxies and network libraries are allowed to repeat a GET on their own. You do not want that to charge you ten times for one question.

---

## In this project

Open `src/RunbookCopilot/Chat.cs` and find `PostAsync`:

```csharp
using var request = new HttpRequestMessage(HttpMethod.Post, uri);
request.Content = new StringContent(body.ToJsonString(ChatRequest.Wire), Encoding.UTF8, "application/json");
```

`HttpMethod.Post`, and the JSON goes in `request.Content` — the body, not the URL.

This is ordinary C#. You have written this before for other APIs. Nothing here is special to AI.

---

## Why this matters beyond the question

This question is really about a bigger idea, and the bigger idea is the foundation of everything else:

**The model has nothing until you send it something.**

There is no stored answer. No session. No memory of you. Every call starts from empty, and the only thing the model knows is what is inside the body you just posted.

Once that is clear, RAG stops being mysterious. RAG is only: *decide what goes in the body.*

---

## Related

- [notes/ai-in-simple-words.md](../ai-in-simple-words.md) — Part 1 and Part 3
- [notes/08-the-request-body.md](../08-the-request-body.md) — the full request, field by field
