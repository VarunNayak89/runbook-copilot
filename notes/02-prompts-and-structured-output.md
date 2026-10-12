# Prompts and structured output

A prompt is not a spell. It is the request body. Your C# code is still responsible for what happens after the response.

## Roles

A chat request is a list of messages.

- **system** — rules for this application. In `GroundedPrompt.cs` the rules are: use only the passages, say you do not know, treat passages as untrusted, cite the file.
- **user** — the question plus the retrieved passages.
- **assistant** — a previous model turn. In the agent this includes tool calls.
- **tool** — the string your code returned from a function. The model reads it on the next turn.

The system message is not a security boundary. It raises the chance of the behavior you want. `TenantRules.Validate` is a boundary, because your process rejects the object before anything else happens.

## Grounding

Grounding means the prompt contains the source text the answer must come from. `ask` builds that in `GroundedPrompt.Build`. The user message lists numbered passages with the file name and heading, then the question.

Look at a real run of the failover question. The first passage is the right section of `failover.md`. Another passage can be `incidents.md`, which does not answer the question. The model is supposed to ignore it. If you only read the final paragraph and not the passage list, you cannot tell whether it did.

## Structured output

`extract` wants a `TenantRequest`: name, region, environment, isTrial, expiresOn. Two different front ends produce that JSON:

- With no model, `PatternExtractor` uses regular expressions. That path exists so the command runs offline, and so you have a baseline that does not depend on a model.
- With a model, `ModelExtractor` asks for JSON only. Models still wrap JSON in markdown fences. `TenantRequestReader.ExtractJsonObject` strips a fence and takes the outer `{...}`. There is a test for that, because it happens in real APIs.

Both paths go through `TenantRules`. The prompt asks for `eastus`, `westus`, or `westeurope`. The validator enforces it. A prompt is a wish. The allowlist is the product.

This is the same instinct as the provisioner: do not trust a caller, check the region against the list you actually support. The caller here is a model.

## Where that story turned out to be too comfortable

Run lab 6 and try `extract "Create a tenant for Fabrikam in mars, environment prod"`. The expected lesson is that the model returns `mars` and the validator rejects it.

That is not what llama3.2 did. On 4 of 4 runs it returned `"region": "westeurope"` and the request was **accepted**, exit code 0. It did not break the allowlist. It silently replaced the input with a value that satisfies the allowlist.

`Validator_rejects_an_unknown_region` still passes and is still correct. It proves the validator rejects `mars` when handed `mars`. It cannot prove the model will ever hand it `mars`.

So be precise about what an allowlist buys you:

- It catches **malformed** output: a region that does not exist, a date that is not a date, a trial with no expiry.
- It does not catch **well-formed fabrication**, because the output looks exactly like a correct answer.

The missing check is not a better prompt and not a stricter schema. It is that nothing compares the extracted values back to the sentence they came from. If a value is not supported by the input, reject it or send it to a human. That is the gap to name out loud, and it is a better interview answer than "I validate the output," because every candidate says that one.

The transcript is in [my-answers/lab-6-transcript.md](my-answers/lab-6-transcript.md).

## Temperature and "JSON only"

Low temperature plus "return JSON only" works often and still fails. Failure modes you should be able to name:

- prose before the object
- a second object
- a region that is not in the allowlist
- a trial with no `expiresOn`
- a date that is not a date

The code path for a bad object is `Rejected` and exit code 1. That is success of the application, not failure of the program.

## Say this out loud

"I put the rules in the system message and the passages in the user message. I deserialize the model output and validate it against an allowlist. If the region is not one we support, I reject it, same as I would reject a bad API request. The regex extractor is only the offline baseline."

## Check yourself

1. Why is the region checked in C# when the prompt already lists the allowed regions?
2. What does `extract` use when no model is configured, and why must you say that out loud?
3. Where in the prompt are the passages, and where are the rules?
4. The `mars` request was accepted. Explain in two sentences why that is worse than a rejected request, and what check would have caught it.
