# Reference

Material from outside this project, written up for offline reading.

## The rule for this folder

This repository is **public on GitHub**, so everything here credits where it came from.

When you find a good article, the note written from it:

1. covers the same **ideas**, in our own words and in the simple English of these notes
2. maps every idea onto **our code**, so it is concrete instead of abstract
3. names and links the original at the top

### About the diagrams

`images/` holds the hand-drawn summary cards from the source document. They are reproduced with credit because they teach the material better than a rewrite would, and the source is a freely readable public link.

If the author of that document ever asks for them to be removed, remove them — the surrounding notes stand on their own without them.

For anything you are not comfortable publishing, use `notes/reference/private/`. That folder is in `.gitignore`, so it stays on your laptop and never reaches GitHub.

To make this whole repository private instead:

```powershell
gh repo edit VarunNayak89/runbook-copilot --visibility private
```

## What is here

| Note | Source | What it adds |
| --- | --- | --- |
| [production-genai-systems.md](production-genai-systems.md) | ["Production GenAI Systems", Google Doc](https://docs.google.com/document/d/12FBU-Ss2Ugo9YCyKXREy9_x5Pudxi8rE4FpuZ91R6Jk/mobilebasic) | The 7 parts of a real AI system. Covers fine-tuning, eval vocabulary, observability, caching and guardrails — topics our other notes do not reach. |
