// ============================================================================
// CopilotTests.cs - 14 tests, and NONE of them need a network or an API key.
//
// "How do you test AI?" is a real interview question. The answer is: you separate
// the parts you can test from the part you cannot.
//
//   CAN test, exactly:   chunking, search, URL building, JSON parsing, validation,
//                        the agent loop, the path check. All deterministic C#.
//
//   CANNOT test exactly: the sentence a model writes. It can change between runs.
//                        That needs an eval and a human, not an assertion.
//
// Most of an "AI application" is in the first group. That is why these tests run
// in 100 milliseconds and give the same result every time.
//
// The fake clients in Chat.cs (ScriptedChatClient, StubChatClient) are what make
// this possible. They are test doubles, the same idea as a mock repository.
// ============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace RunbookCopilot.Tests;

public class CopilotTests
{
    [Fact]
    public void Chunker_splits_on_headings_and_keeps_the_source_file()
    {
        var chunks = new MarkdownChunker().Chunk("sample.md", "# One\n\nalpha\n\n# Two\n\nbeta\n");

        Assert.Equal(2, chunks.Count);
        Assert.Equal("One", chunks[0].Heading);
        Assert.Contains("alpha", chunks[0].Text);
        Assert.Equal("sample.md", chunks[0].Source);
        Assert.Equal("Two", chunks[1].Heading);
    }

    [Fact]
    public void Long_section_is_split_into_more_than_one_chunk()
    {
        var body = string.Join("\n\n", Enumerable.Range(0, 40).Select(i => $"paragraph-{i} " + new string('x', 80)));
        var chunks = new MarkdownChunker(maxChars: 500, overlapChars: 50).Chunk("long.md", "# Topic\n\n" + body);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.Equal("long.md", chunk.Source));
    }

    [Fact]
    public async Task Hashing_embedder_is_stable()
    {
        var embedder = new HashingEmbedder();
        var first = await embedder.EmbedAsync("trial tenant storage redundancy", CancellationToken.None);
        var second = await embedder.EmbedAsync("trial tenant storage redundancy", CancellationToken.None);

        Assert.Equal(first, second);
        Assert.InRange(VectorMath.Cosine(first, first), 0.99, 1.01);
    }

    /// <summary>
    /// The eval, run as a unit test. This is the important one.
    ///
    /// It means a change that breaks search FAILS THE BUILD, instead of being
    /// discovered later by a user getting a confidently wrong answer.
    /// Treat retrieval quality as something you test, not something you hope for.
    /// </summary>
    [Fact]
    public async Task Retrieval_eval_recalls_every_runbook()
    {
        var contentRoot = ContentPaths.Find();
        var knowledge = await KnowledgeBase.LoadAsync(contentRoot, new HashingEmbedder(), CancellationToken.None);
        var questions = JsonSerializer.Deserialize<List<EvalQuestion>>(
            await File.ReadAllTextAsync(Path.Combine(contentRoot, "eval", "questions.json")),
            JsonDefaults.Options)!;
        var report = await new RetrievalEval(knowledge.Search).RunAsync(questions, topK: 3, CancellationToken.None);

        Assert.Equal(1, report.Recall);
    }

    [Fact]
    public void Prompt_treats_passages_as_untrusted_and_includes_the_source()
    {
        var hits = new[]
        {
            new ScoredChunk(new Chunk("1", "trials.md", "Duration", "A trial tenant lasts 14 days."), 0.5)
        };
        var prompt = GroundedPrompt.Build("How many days does a trial tenant last?", hits);

        Assert.Contains("untrusted", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not know", prompt.System, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("trials.md", prompt.User);
        Assert.Contains("14 days", prompt.User);
    }

    [Fact]
    public void Azure_and_compatible_endpoints_build_different_urls()
    {
        var azure = ModelEndpoint.For(new CopilotConfig
        {
            Provider = "AzureOpenAI",
            Endpoint = "https://example.openai.azure.com/",
            ApiKey = "test-key",
            ChatModel = "gpt-4o-mini",
            EmbeddingModel = "text-embedding-3-small",
            ApiVersion = "2024-10-21"
        });
        var local = ModelEndpoint.For(new CopilotConfig
        {
            Provider = "OpenAICompatible",
            Endpoint = "http://localhost:11434/v1",
            ApiKey = "ollama",
            ChatModel = "llama3.2",
            EmbeddingModel = "nomic-embed-text"
        });

        Assert.Equal(
            "https://example.openai.azure.com/openai/deployments/gpt-4o-mini/chat/completions?api-version=2024-10-21",
            azure.ChatCompletions);
        Assert.Equal("api-key", azure.ApiKeyHeader);
        Assert.Equal("test-key", azure.ApiKeyValue);
        Assert.Equal("http://localhost:11434/v1/chat/completions", local.ChatCompletions);
        Assert.Equal("http://localhost:11434/v1/embeddings", local.Embeddings);
        Assert.Equal("Authorization", local.ApiKeyHeader);
        Assert.Equal("Bearer ollama", local.ApiKeyValue);
    }

    [Fact]
    public void Pattern_extractor_accepts_a_valid_trial_sentence()
    {
        var request = PatternExtractor.Extract(
            "Create a trial tenant for Contoso in eastus, environment dev, ending on 2026-11-01");

        Assert.Equal("Contoso", request.Name);
        Assert.Equal("eastus", request.Region);
        Assert.Equal("dev", request.Environment);
        Assert.True(request.IsTrial);
        Assert.Equal("2026-11-01", request.ExpiresOn);
        Assert.Empty(TenantRules.Validate(request));
    }

    /// <summary>
    /// The validator rejects "mars".
    ///
    /// READ THE LIMIT OF THIS TEST CAREFULLY.
    /// It proves the validator rejects "mars" WHEN IT IS GIVEN "mars".
    /// It cannot prove a model will ever give it "mars".
    ///
    /// We tested that with a real model. Asked for a tenant in "mars", llama3.2
    /// returned "westeurope" instead - a legal value - and this validator accepted it,
    /// 4 times out of 4. The test passes. The system is still wrong.
    ///
    /// A green test tells you the code does what you wrote. It does not tell you
    /// that you wrote enough code. See notes/my-answers/lab-6-transcript.md.
    /// </summary>
    [Fact]
    public void Validator_rejects_an_unknown_region()
    {
        var request = new TenantRequest
        {
            Name = "Contoso",
            Region = "mars",
            Environment = "dev",
            IsTrial = false
        };

        var errors = TenantRules.Validate(request);
        Assert.Contains(errors, error => error.Contains("mars", StringComparison.Ordinal));
    }

    /// <summary>
    /// Models wrap JSON in a ```json fence even when told not to. This proves we cope.
    ///
    /// StubChatClient lets us hand the app the exact broken output we want to test,
    /// with no network and no randomness. That is why a fake model is worth having.
    /// </summary>
    [Fact]
    public async Task Model_json_wrapped_in_a_fence_still_validates()
    {
        var stub = new StubChatClient("""
            ```json
            {"name":"Contoso","region":"eastus","environment":"dev","isTrial":true,"expiresOn":"2026-11-01"}
            ```
            """);
        var json = await ModelExtractor.CompleteAsync(stub, "create a trial for Contoso", CancellationToken.None);
        var request = TenantRequestReader.Read(json);

        Assert.Empty(TenantRules.Validate(request));
    }

    [Fact]
    public async Task Scripted_agent_calls_status_then_search()
    {
        var contentRoot = ContentPaths.Find();
        var knowledge = await KnowledgeBase.LoadAsync(contentRoot, new HashingEmbedder(), CancellationToken.None);
        var services = await ServiceCatalog.LoadAsync(contentRoot, CancellationToken.None);
        var tools = new ToolCatalog(knowledge, services, Path.Combine(contentRoot, "runbooks"));
        var loop = new AgentLoop(new ScriptedChatClient(services.Names), tools);

        var result = await loop.RunAsync(
            "Is billing-api healthy and where is failover documented?",
            CancellationToken.None);

        Assert.Contains(result.Steps, step => step.Detail.Contains("get_service_status", StringComparison.Ordinal));
        Assert.Contains(result.Steps, step => step.Detail.Contains("search_runbooks", StringComparison.Ordinal));
        Assert.Contains("healthy", result.Answer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// SECURITY TEST. The file tool must refuse "..\secrets.md".
    ///
    /// A document could contain a sentence telling the model to read another path,
    /// and the model might follow it. This test proves the tool says no regardless.
    ///
    /// The lesson: the control is the CODE in the tool, not the polite sentence in
    /// the system prompt. You can test code. You cannot test a wish.
    /// </summary>
    [Fact]
    public async Task Runbook_tool_refuses_a_path()
    {
        var contentRoot = ContentPaths.Find();
        var knowledge = await KnowledgeBase.LoadAsync(contentRoot, new HashingEmbedder(), CancellationToken.None);
        var services = await ServiceCatalog.LoadAsync(contentRoot, CancellationToken.None);
        var tools = new ToolCatalog(knowledge, services, Path.Combine(contentRoot, "runbooks"));

        var result = await tools.InvokeAsync("get_runbook", """{"name":"..\\secrets.md"}""", CancellationToken.None);

        Assert.Contains("file name", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ask_request_carries_the_passages_and_no_tools()
    {
        var hits = new[]
        {
            new ScoredChunk(new Chunk("1", "trials.md", "Duration", "A trial tenant lasts 14 days."), 0.5)
        };
        var prompt = GroundedPrompt.Build("How many days does a trial tenant last?", hits);

        var body = ChatRequest.Build(
            "some-model",
            new[] { new ChatMessage("system", prompt.System), new ChatMessage("user", prompt.User) },
            tools: null);

        var messages = body["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0]!["role"]!.GetValue<string>());
        Assert.Equal("user", messages[1]!["role"]!.GetValue<string>());
        Assert.Contains("14 days", messages[1]!["content"]!.GetValue<string>(), StringComparison.Ordinal);

        // The retrieved text travels as ordinary string content. There is no second channel to the corpus.
        Assert.Null(body["tools"]);
    }

    [Fact]
    public async Task Agent_turns_append_a_tool_message_that_answers_a_tool_call()
    {
        var contentRoot = ContentPaths.Find();
        var knowledge = await KnowledgeBase.LoadAsync(contentRoot, new HashingEmbedder(), CancellationToken.None);
        var services = await ServiceCatalog.LoadAsync(contentRoot, CancellationToken.None);
        var tools = new ToolCatalog(knowledge, services, Path.Combine(contentRoot, "runbooks"));
        var recorder = new RecordingChatClient(new ScriptedChatClient(services.Names), "some-model");

        await new AgentLoop(recorder, tools).RunAsync(
            "Is billing-api healthy and where is failover documented?",
            CancellationToken.None);

        Assert.True(recorder.Requests.Count > 1, "the loop should post more than one request");

        var first = recorder.Requests[0]["messages"]!.AsArray();
        var last = recorder.Requests[^1]["messages"]!.AsArray();
        Assert.Equal(2, first.Count);
        Assert.True(last.Count > first.Count, "the messages array grows as tool results come back");

        // Every turn re-sends the same tool list. The model never keeps state for you.
        Assert.All(recorder.Requests, request => Assert.Equal(3, request["tools"]!.AsArray().Count));

        var toolMessage = last.First(message => message!["role"]!.GetValue<string>() == "tool");
        var answeredId = toolMessage!["tool_call_id"]!.GetValue<string>();
        var requestedIds = last
            .Where(message => message!["tool_calls"] is JsonArray)
            .SelectMany(message => message!["tool_calls"]!.AsArray())
            .Select(call => call!["id"]!.GetValue<string>());

        Assert.Contains(answeredId, requestedIds);
    }

    [Fact]
    public void Answer_rubric_flags_a_missing_citation()
    {
        var failures = AnswerRubric.Check("A trial lasts 14 days.", "trials.md", "14 days");

        Assert.Contains(failures, failure => failure.Contains("trials.md", StringComparison.Ordinal));
    }
}
