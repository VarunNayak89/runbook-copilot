"""A LangChain Embeddings class that mirrors HashingEmbedder in the C# app.

Why this exists instead of a real embedding model:

LangChain's Embeddings is an interface, the same way IEmbedder is an interface in C#.
Implementing it with the exact algorithm from the C# side means the Python port
retrieves from the same corpus with the same signal, so any difference you see in
the results comes from the pipeline, not from swapping in a better embedder.

It also means this port runs with no model download, like the C# one.

This is not a good embedder. It is a bag of words with a sign. Note 3 explains why.
Swap it for OllamaEmbeddings(model="nomic-embed-text") when you want to feel the
difference on a paraphrased question. The rest of the file does not change, which
is the whole point of an interface.
"""

from langchain_core.embeddings import Embeddings


def tokens(text: str) -> list[str]:
    """Letters and digits, lowercased, everything else is a separator.

    Same rule as Tokenizer.Tokens in Chunking.cs.
    """
    out: list[str] = []
    current: list[str] = []
    for ch in text.lower():
        if ch.isalnum():
            current.append(ch)
        elif current:
            out.append("".join(current))
            current = []
    if current:
        out.append("".join(current))
    return out


def fnv1a(text: str) -> int:
    """32-bit FNV-1a. C# uint arithmetic wraps, so mask to 32 bits on every step."""
    hash_value = 2166136261
    for ch in text:
        hash_value ^= ord(ch)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return hash_value


class HashingEmbeddings(Embeddings):
    """Feature hashing: each word adds or subtracts 1 in a bucket chosen by its hash."""

    def __init__(self, dimensions: int = 384) -> None:
        if dimensions < 32:
            raise ValueError("dimensions must be at least 32")
        self.dimensions = dimensions

    @property
    def name(self) -> str:
        return f"hashing-{self.dimensions}"

    def embed_query(self, text: str) -> list[float]:
        vector = [0.0] * self.dimensions
        for token in tokens(text):
            hash_value = fnv1a(token)
            bucket = hash_value % self.dimensions
            sign = 1.0 if (hash_value & 0x80000000) == 0 else -1.0
            vector[bucket] += sign
        return vector

    def embed_documents(self, texts: list[str]) -> list[list[float]]:
        return [self.embed_query(text) for text in texts]
