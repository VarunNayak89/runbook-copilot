# How to study this

You already call HTTP APIs, validate input, and ship a CLI. An LLM application is that kind of program with a model behind one of the calls. The new part is knowing what the model will not do for you.

## What "I built this" has to survive

An interviewer will ask you to open a file and explain a choice. They will ask what broke. They will ask what you measured. A folder you have not changed fails that conversation even if the tests are green.

### First, the thing that is easy to forget

This code was written **with an AI assistant**, in a chat session. You directed it and you asked the questions. You did not type it.

That is fine, and it is normal engineering in 2026. But it is also **public in your own git history** — every commit carries `Co-Authored-By: Claude`. Anyone can see it in ten seconds. So "I wrote every line" is a sentence your own repository contradicts.

Keep the honest framing: *"I built this with an AI assistant, and I can explain any part of it."* That sentence survives scrutiny. The other one does not.

### Three levels, and you stop at the one you have actually done

1. **Read and run.** You have run the commands and can explain what an LLM application sends, how retrieval works, and how it is scored. You are studying this, not claiming to have shipped it.
2. **Changed and measured.** After labs 2, 3 and 4: you altered the corpus, the chunk size and the tools, and you can say what the score did and why. **This is where it starts being yours.**
3. **Found and fixed.** After lab 6 and lab 7: you called a real model, found a real failure, and closed it. Now "I built this" is defensible.

The line is not who typed the code. It is: **can you defend every decision, and did you change something and measure the result?**

Say the level you are on. The stand-in model is a test double. Calling it an LLM is the kind of slip that ends the interview.

LangChain is a library, mostly Python. The C# app does not use it. [Note 5](05-libraries.md) teaches the vocabulary so a job post is readable, and [projects/langchain-port](../projects/langchain-port/README.md) is `ask` rebuilt with the real library, scoring the same 8/8 on the same questions. Run that port yourself before you claim the word. Reading that it exists is not the same as having run it.

Nothing in the provisioning tool at my day job is RAG or an agent. Do not describe this study project as production work you did at work.

## How a session works

Read one note. Run the command it names. Answer the questions at the bottom in `notes/my-answers/` without copying the note. If you cannot answer, that note is not finished.

Bring the answer file back here and we will correct the gaps. That is the loop that makes the theory stick to the code.

## Words you will meet

| Word | In this folder |
| --- | --- |
| LLM | A model that continues text. You call it. You do not train it. |
| Token | A chunk of a word the model counts. English is often about 4 characters per token. |
| Context window | The maximum tokens in one request, including the passages you paste in. |
| Prompt | The messages you send: system, user, and later tool results. |
| RAG | Search your documents, paste the hits into the prompt, then generate. |
| Embedding | A vector of numbers for a piece of text. Similar text, similar direction. |
| Chunk | One piece of a document small enough to retrieve and paste. |
| Citation | The source file name printed with the answer. |
| Tool call | The model asks your code to run a function, then reads the result. |
| Agent | Your loop: model, maybe a tool, model again, until it stops or you stop it. |
| Eval | A scored set of questions. Here, recall@3 on `content/eval/questions.json`. |

## Check yourself

1. Which of the three levels are you on today, and which sentence are you not allowed to say yet?
2. What does recall@3 measure in `eval`, in one sentence?
3. Why is this project in a personal folder instead of a work repository?
