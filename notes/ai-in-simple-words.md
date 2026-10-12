# AI in simple words

This note uses short sentences and easy words.

It explains five words:

1. LLM
2. LLM application
3. RAG
4. Agent
5. LangChain

Read one part. Stop. Make sure you understand it. Then read the next part.

You are a software developer. So I will compare AI to things you already know.

---

# Part 1: LLM

## What it is

LLM means **Large Language Model**.

Think of it as a function:

```
text  →  [ LLM ]  →  text
```

You send text. You get text back. That is all.

It is a **web API**. You call it with HTTP POST. Like any other API you use.

## Important

You do **not** install the model on your computer.
You do **not** train the model.
You only **call** it.

This is the same as a payment API. You do not build the bank. You call the bank.

## What does the LLM know?

Imagine a new employee on their first day.

- This person read millions of books. They know many general things.
- This person **never saw your company documents**.
- This person **cannot open files** on your computer.
- This person answers **from memory only**.

One more thing. This person is very polite. They always give an answer. If they do not know, they **guess**. The guess sounds confident and correct. But it is wrong.

This wrong-but-confident answer has a name: **hallucination**.

## In our project

Open `src/RunbookCopilot/Chat.cs`. Look at `OpenAiChatClient`.

It is only an HTTP client. It builds JSON. It does POST. It reads the answer.

That is the whole LLM part. There is no magic inside.

## One line to remember

> An LLM is a web API. You send text, you get text. It knows general things. It does not know your company.

---

# Part 2: LLM application

## What it is

An **LLM application** is a normal program. One step inside it calls an LLM.

That is the whole definition.

## Compare to something you know

You already built programs like this:

```
read input  →  validate  →  call payment API  →  check result  →  save to database
```

An LLM application looks the same:

```
read question  →  find documents  →  call LLM API  →  check answer  →  show result
```

Only **one box** changed. The payment API became the LLM API.

So you already know 90% of this. You write the program. The LLM is one step inside it.

## Why your normal code still matters

The LLM gives you text. Text can be wrong.

So your code must still:

- check the answer
- validate the data
- reject bad values

This is the same as before. You never trusted a user input. Now you also do not trust the model output.

## In our project

Open `src/RunbookCopilot/Program.cs`.

The `ask` command does this:

1. Find the question in the command line.
2. Search the runbook files.
3. Build the prompt text.
4. Call the LLM.
5. Print the answer.

Step 4 is the LLM. Steps 1, 2, 3 and 5 are normal C#.

## One line to remember

> An LLM application is a normal program. One step calls the LLM. The rest is your usual code.

---

# Part 3: RAG

## The problem first

Always understand the problem before the solution.

The LLM never saw your company documents. (Part 1.)

So if you ask:

> "Which region is paired with eastus for failover?"

The LLM does not know. Northwind Cloud is your private company. It is not in any book.

But the LLM is polite. It will **guess**. It will say something that sounds correct. That is bad.

## The solution

Very simple idea:

**Before you ask the question, give the model the correct pages.**

Like this. A new employee does not know your rules. So you do this:

1. You go to the manual.
2. You photocopy the 4 correct pages.
3. You put the pages on their desk.
4. Then you ask the question.

Now the employee can answer. They read the pages. They do not guess.

**That is RAG. That is the whole idea.**

## What the letters mean

RAG = **R**etrieval **A**ugmented **G**eneration.

| Letter | Word | Simple meaning |
| --- | --- | --- |
| R | Retrieval | Find the correct pages |
| A | Augmented | Add the pages to the question |
| G | Generation | The model writes the answer |

Read it backwards and it is a sentence: *Generation, that is Augmented by Retrieval.*

## In our project

Run this:

```powershell
dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
```

You will see the word `Passages`. Under it there are 4 sections of text.

Those 4 sections are the photocopied pages. Your code found them and copied them.

Then they are sent to the model together with the question.

## The most important rule of RAG

If step R (find the pages) picks the **wrong** pages, then the correct answer is **not in the request at all**.

The model cannot fix this. The answer is simply not there.

So: **first make the search good. Then worry about the model.**

This is why we have an `eval` command. It only tests the search. It does not test the model.

```powershell
dotnet run --project src/RunbookCopilot -- eval
```

It prints `Recall@3: 8/8 = 1.00`. This means: for all 8 test questions, the correct file was found. Only the search was tested here.

## One line to remember

> RAG = find the correct pages, paste them into the question, then ask the model.

---

# Part 4: Agent

## The problem first

In RAG, **you** decide which pages to give. You decide before you call the model.

But sometimes you do not know what is needed.

Example question:

> "Is billing-api healthy, and where is failover documented?"

This needs two different things:

1. The current status of a service. (This is in a JSON file, not in a document.)
2. A document about failover.

One search cannot do both.

## The solution

Give the model a **list of functions it may ask for**.

Then the conversation looks like this:

| Who | What happens |
| --- | --- |
| You | "Here is the question. You may ask for these 3 functions." |
| Model | "Please run `get_service_status` with service = billing-api." |
| Your code | Runs the C# function. Sends the result back. |
| Model | "Please run `get_runbook` with name = failover.md." |
| Your code | Runs it. Sends the result back. |
| Model | "OK. Here is my final answer." |

This repeating is called a **loop**. The program with this loop is called an **agent**.

## Very important

The model **cannot run anything**.

The model only sends **text**. The text says: *"please run this function"*.

**Your C# code** decides to run it or not. Your code has the permission. The model has zero permission.

Think of it like a help desk ticket. The customer writes "please restart the server". The customer cannot restart it. You read the ticket and you decide.

## In our project

Run this:

```powershell
dotnet run --project src/RunbookCopilot -- agent --scripted "Is billing-api healthy and where is failover documented?"
```

You will see `Step 1`, `Step 2`, `Step 3`. That is the loop.

The functions are in `src/RunbookCopilot/Agent.cs`, in the class `ToolCatalog`. There are 3:

- `search_runbooks` — search the documents
- `get_service_status` — read the status file
- `get_runbook` — open one document

The loop is in the same file, in `AgentLoop`.

## Why there is a step limit

Look at `AgentLoop`. It has `maxSteps = 4`.

Every step is one more API call. Every API call costs money and time.

A confused model can ask for functions again and again. The limit stops it.

## One line to remember

> An agent = a loop. The model asks for a function. Your code runs it. Your code sends the result back. Repeat.

---

# Part 5: LangChain

## What it is

LangChain is a **library**. It is mostly for Python.

It contains ready-made code for the boring parts of RAG:

- read files
- split text into pieces
- search the pieces
- build the prompt
- call the model

## Compare to something you know

In .NET you can talk to a database in two ways:

1. Write SQL and `SqlCommand` by hand. You control everything. You write more code.
2. Use Entity Framework. It writes the SQL for you. You write less code.

LangChain is the second one, but for RAG.

**Entity Framework did not teach you what a database is.** You had to understand tables and joins first.

LangChain is the same. It does not teach you what RAG is. You must understand RAG first. (You did — Part 3.)

## Important

LangChain is **not** a new technology.

Whatever LangChain does, it ends with the same thing: an HTTP POST with your text inside. Exactly like our C# code.

## In our project

We built the same `ask` command two times:

| Folder | Language | Library |
| --- | --- | --- |
| `projects/RunbookCopilot` | C# | none, written by hand |
| `projects/langchain-port` | Python | LangChain |

Both use the same documents. Both get the same score, 8 out of 8.

You can run the Python one:

```powershell
cd projects\langchain-port
.\.venv\Scripts\python.exe ask.py --eval
```

## What LangChain decides, and what it does not

| LangChain writes this for you | You must still decide this |
| --- | --- |
| Code to read files | How big each text piece should be |
| Code to split text | Which pages to send |
| Code to search | What the instructions say |
| Code to call the model | How you test if it works |

The second column is the difficult part. The library does not help there.

## One line to remember

> LangChain is a library. It writes the boring code for you. It does not decide the important things.

---

# All five together

Now read this table. It should make sense now.

| Word | Simple meaning | File in our project |
| --- | --- | --- |
| LLM | A web API. Text in, text out. | `Chat.cs` |
| LLM application | A normal program. One step calls the LLM. | `Program.cs` |
| RAG | Find the right pages. Paste them in the question. | `Search.cs` + `GroundedPrompt.cs` |
| Agent | A loop. The model asks for functions. Your code runs them. | `Agent.cs` |
| LangChain | A library that writes the boring code. | `projects/langchain-port` |

---

# Check yourself

Do not write long answers. Two or three sentences is enough. Use your own words.

1. Why does the LLM not know the answer about Northwind Cloud?
2. In RAG, what does the letter R mean, and what happens if R picks the wrong pages?
3. Can the model run a C# function by itself? Yes or no. Explain in one sentence.
4. Name one thing LangChain writes for you, and one thing it does not decide.

Write your answers in `notes/my-answers/simple-words.md`.
