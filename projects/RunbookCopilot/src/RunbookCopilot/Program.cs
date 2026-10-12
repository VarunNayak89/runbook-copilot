// ============================================================================
// Program.cs - the entry point. Start reading the project here.
//
// Five commands:
//
//   eval     score the SEARCH only. No model. Fast, free, repeatable.
//   ask      RAG: search, then (if a model is configured) answer.
//   agent    the tool loop: the model asks for functions, we run them.
//   extract  turn a sentence into a validated JSON object.
//   trace    print the HTTP request body instead of sending it. Sends nothing.
//
// Notice how ordinary this file is. Reading arguments, loading files, printing
// to the console. An "LLM application" is a normal program; only one step inside
// it calls a model, and you can see exactly which step.
// ============================================================================

using System.Text.Json;

namespace RunbookCopilot;

public static class Program
{
    /// <summary>
    /// The return value is the EXIT CODE, and it is not decoration:
    ///   0 = success
    ///   1 = the request was rejected, or the arguments were wrong
    ///   2 = eval ran but the score was not perfect
    /// A build pipeline can read these and fail automatically.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        var scripted = args.Any(arg => arg.Equals("--scripted", StringComparison.OrdinalIgnoreCase));
        var positional = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToArray();
        var command = positional.Length == 0 ? "help" : positional[0].ToLowerInvariant();
        var text = string.Join(" ", positional.Skip(1));

        try
        {
            return command switch
            {
                "ask" => await AskAsync(text),
                "eval" => await EvalAsync(),
                "agent" => await AgentAsync(text, scripted),
                "extract" => await ExtractAsync(text),
                "trace" => await TraceAsync(text),
                "help" => Help(0),
                _ => Help(1)
            };
        }
        // We catch only the exceptions we EXPECT and have written good messages for:
        // the model was unreachable, the JSON was broken, the content folder was missing.
        //
        // Anything else is a real bug, and it is allowed to crash with a full stack
        // trace. Catching everything would hide bugs behind a tidy message.
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or JsonException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// THE RAG COMMAND. Read this method to understand RAG in one screen.
    ///
    ///   R - SearchAsync finds the 4 best passages
    ///   A - GroundedPrompt.Build puts them into the question
    ///   G - the chat client sends it and the model writes
    ///
    /// If no model is configured it stops after R and A, and prints what it found.
    /// That offline mode is honest and still shows you the important half.
    /// </summary>
    private static async Task<int> AskAsync(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Help(1);
        }

        var contentRoot = ContentPaths.Find();
        var config = CopilotConfig.Load(contentRoot);

        // A timeout is not optional. Models can be slow, and without this the app
        // would hang forever on a bad network instead of failing with a clear message.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        // Read, chunk, embed and index every runbook. See KnowledgeBase.cs.
        var knowledge = await LoadKnowledgeAsync(contentRoot, config, http);

        // --- R: Retrieval ---
        // Why 4? It is a budget decision. More passages means a better chance the
        // answer is present, but more tokens to pay for and more noise to confuse
        // the model. 4 is a reasonable default for a small corpus, not a law.
        var hits = await knowledge.Search.SearchAsync(question, 4, CancellationToken.None);

        // --- A: Augmented ---
        // Build the two message strings. Nothing has been sent yet.
        var prompt = GroundedPrompt.Build(question, hits);

        Console.WriteLine($"Retrieval: hybrid (BM25 + {knowledge.Search.EmbeddingName}, fused with reciprocal rank fusion)");
        Console.WriteLine(config.UseRemoteChat
            ? $"Generator: {config.Provider} / {config.ChatModel}"
            : "Generator: none (passages only)");
        Console.WriteLine();
        Console.WriteLine("Question");
        Console.WriteLine(question);
        Console.WriteLine();

        // We ALWAYS print the passages, even when a model answers below.
        //
        // This is a deliberate product decision, not debug output. A model can write a
        // fluent sentence with a citation next to it and still be wrong. Printing the
        // source text beside the answer is what lets a human check it in two seconds.
        PrintPassages(hits);

        // --- G: Generation. Only if a model is configured. ---
        if (!config.UseRemoteChat)
        {
            Console.WriteLine("Answer");
            Console.WriteLine("No chat model is configured. The passages above are what a model would be allowed to see.");
            Console.WriteLine("Set COPILOT_PROVIDER, COPILOT_ENDPOINT, and COPILOT_CHAT_MODEL when you want a generated answer.");
            return 0;
        }

        var chat = new OpenAiChatClient(http, ModelEndpoint.For(config));

        // tools: null is the whole difference between `ask` and `agent`.
        // With no tools the model has exactly one option: answer. It cannot ask for
        // anything, cannot loop, and this costs exactly one API call. That predictability
        // is why you prefer this shape whenever you already know the steps.
        var turn = await chat.CompleteAsync(
            new[] { new ChatMessage("system", prompt.System), new ChatMessage("user", prompt.User) },
            tools: null,
            CancellationToken.None);
        Console.WriteLine("Answer");
        Console.WriteLine(turn.Content);
        Console.WriteLine();
        Console.WriteLine("Check every claim against the passages. A citation is not proof if the sentence is not in that file.");
        return 0;
    }

    private static async Task<int> EvalAsync()
    {
        var contentRoot = ContentPaths.Find();
        var config = CopilotConfig.Load(contentRoot);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var knowledge = await LoadKnowledgeAsync(contentRoot, config, http);
        var questions = JsonSerializer.Deserialize<List<EvalQuestion>>(
            await File.ReadAllTextAsync(Path.Combine(contentRoot, "eval", "questions.json")),
            JsonDefaults.Options) ?? new List<EvalQuestion>();
        var report = await new RetrievalEval(knowledge.Search).RunAsync(questions, topK: 3, CancellationToken.None);

        Console.WriteLine($"Retrieval eval, top 3, embeddings = {knowledge.Search.EmbeddingName}");
        foreach (var result in report.Cases)
        {
            var mark = result.Pass ? "PASS" : "FAIL";
            Console.WriteLine($"{mark}  expected {result.ExpectedSource,-14} top {result.TopSource,-22} {result.Question}");
        }

        Console.WriteLine();
        Console.WriteLine($"Recall@3: {report.Passed}/{report.Cases.Count} = {report.Recall:0.00}");
        Console.WriteLine("This score measures search, not the wording of a generated answer.");

        // Exit code 2 when the score is not perfect, so a build pipeline can fail
        // automatically. This turns the eval into a test, which is the whole point.
        return report.Recall == 1 ? 0 : 2;
    }

    private static async Task<int> AgentAsync(string question, bool scripted)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return Help(1);
        }

        var contentRoot = ContentPaths.Find();
        var config = CopilotConfig.Load(contentRoot);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var knowledge = await LoadKnowledgeAsync(contentRoot, config, http);
        var services = await ServiceCatalog.LoadAsync(contentRoot, CancellationToken.None);
        var tools = new ToolCatalog(knowledge, services, Path.Combine(contentRoot, "runbooks"));

        IChatClient chat;
        if (scripted || !config.UseRemoteChat)
        {
            if (!scripted && !config.UseRemoteChat)
            {
                Console.WriteLine("No chat model is configured. Add --scripted to walk the tool loop with a stand-in model.");
                Console.WriteLine("A stand-in does not understand the question. It only shows the loop.");
                return 1;
            }

            chat = new ScriptedChatClient(services.Names);
            Console.WriteLine("Model: scripted stand-in. This is not an LLM.");
        }
        else
        {
            chat = new OpenAiChatClient(http, ModelEndpoint.For(config));
            Console.WriteLine($"Model: {config.Provider} / {config.ChatModel}");
        }

        // The loop itself. All the interesting behaviour is in AgentLoop.RunAsync.
        var result = await new AgentLoop(chat, tools).RunAsync(question, CancellationToken.None);

        // Print every step. An agent that only shows its final answer is impossible to
        // debug and impossible to trust: you cannot tell which tool it called, what came
        // back, or where it went wrong. Always make the loop visible.
        foreach (var step in result.Steps)
        {
            Console.WriteLine();
            Console.WriteLine($"Step {step.Number} ({step.Kind})");
            Console.WriteLine(step.Detail);
        }

        return 0;
    }

    private static async Task<int> ExtractAsync(string sentence)
    {
        if (string.IsNullOrWhiteSpace(sentence))
        {
            return Help(1);
        }

        var contentRoot = ContentPaths.Find();
        var config = CopilotConfig.Load(contentRoot);
        string json;
        if (config.UseRemoteChat)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var chat = new OpenAiChatClient(http, ModelEndpoint.For(config));
            Console.WriteLine($"Extractor: {config.Provider} / {config.ChatModel}");
            json = await ModelExtractor.CompleteAsync(chat, sentence, CancellationToken.None);
        }
        else
        {
            Console.WriteLine("Extractor: pattern baseline. This is a regular expression, not a model.");
            Console.WriteLine("The validator below is the same gate a model response must pass.");
            json = JsonSerializer.Serialize(PatternExtractor.Extract(sentence), JsonDefaults.Options);
        }

        Console.WriteLine();
        Console.WriteLine(json);
        TenantRequest request;
        try
        {
            request = TenantRequestReader.Read(json);
        }
        catch (JsonException ex)
        {
            Console.WriteLine();
            Console.WriteLine("Rejected. The output was not a tenant request.");
            Console.WriteLine(ex.Message);
            return 1;
        }

        // The validator runs on BOTH paths, regex and model. This is the gate.
        var errors = TenantRules.Validate(request);
        Console.WriteLine();
        if (errors.Count == 0)
        {
            Console.WriteLine("Accepted.");
            return 0;
        }

        // A rejection is the application WORKING, not failing. We caught bad data
        // before it reached anything that matters. Exit code 1 says so to a script.
        Console.WriteLine("Rejected.");
        foreach (var error in errors)
        {
            Console.WriteLine("- " + error);
        }

        return 1;
    }

    /// <summary>
    /// Prints the request body instead of sending it, so the pipeline stops being a diagram.
    /// `trace ask` is one turn with no tools. `trace agent` is the whole loop, one body per turn.
    /// </summary>
    private static async Task<int> TraceAsync(string text)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var target = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        var question = parts.Length > 1 ? parts[1] : "";
        if (string.IsNullOrWhiteSpace(question) || (target != "ask" && target != "agent"))
        {
            return Help(1);
        }

        var contentRoot = ContentPaths.Find();
        var config = CopilotConfig.Load(contentRoot);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var knowledge = await LoadKnowledgeAsync(contentRoot, config, http);
        var model = config.UseRemoteChat ? config.ChatModel : "<COPILOT_CHAT_MODEL>";
        var url = config.UseRemoteChat
            ? ModelEndpoint.For(config).ChatCompletions
            : "<COPILOT_ENDPOINT>/chat/completions";

        Console.WriteLine("Nothing is sent. This is the body that would be posted.");
        Console.WriteLine($"POST {url}");
        Console.WriteLine("Content-Type: application/json");
        Console.WriteLine();

        if (target == "ask")
        {
            var hits = await knowledge.Search.SearchAsync(question, 4, CancellationToken.None);
            var prompt = GroundedPrompt.Build(question, hits);
            var body = ChatRequest.Build(
                model,
                new[] { new ChatMessage("system", prompt.System), new ChatMessage("user", prompt.User) },
                tools: null);

            Console.WriteLine("Request 1 of 1. No tools array, so the model answers or it does not. That is RAG.");
            Console.WriteLine(body.ToJsonString(ChatRequest.Readable));
            Console.WriteLine();
            Console.WriteLine($"Retrieval put {hits.Count} passages inside messages[1].content: "
                + string.Join(", ", hits.Select(hit => hit.Chunk.Source).Distinct()));
            Console.WriteLine("That text is the model's only access to content/runbooks. Nothing else was uploaded.");
            return 0;
        }

        var services = await ServiceCatalog.LoadAsync(contentRoot, CancellationToken.None);
        var tools = new ToolCatalog(knowledge, services, Path.Combine(contentRoot, "runbooks"));
        var recorder = new RecordingChatClient(new ScriptedChatClient(services.Names), model);
        await new AgentLoop(recorder, tools).RunAsync(question, CancellationToken.None);

        Console.WriteLine($"The scripted stand-in drove {recorder.Requests.Count} turns. Each turn is one POST.");
        for (var i = 0; i < recorder.Requests.Count; i++)
        {
            var messageCount = recorder.Requests[i]["messages"]!.AsArray().Count;
            Console.WriteLine();
            Console.WriteLine($"Request {i + 1} of {recorder.Requests.Count}, messages = {messageCount}");
            Console.WriteLine(recorder.Requests[i].ToJsonString(ChatRequest.Readable));
        }

        Console.WriteLine();
        Console.WriteLine("Read the messages array growing. Your code appends two things after every tool call:");
        Console.WriteLine("  an assistant message carrying tool_calls, then a message with role \"tool\" and the matching tool_call_id.");
        Console.WriteLine("The tools array is identical on every turn. The model only names a function. Your C# runs it.");
        return 0;
    }

    /// <summary>
    /// Choose the embedder, then build the knowledge base.
    ///
    /// This is the interface seam doing its job. Two completely different implementations
    /// - one local and fake, one remote and real - and the line below does not care which.
    /// KnowledgeBase.LoadAsync does not care either. Only this one `?:` knows.
    /// </summary>
    private static async Task<KnowledgeBase> LoadKnowledgeAsync(string contentRoot, CopilotConfig config, HttpClient http)
    {
        IEmbedder embedder = config.UseRemoteEmbeddings
            ? new OpenAiEmbedder(http, ModelEndpoint.For(config))
            : new HashingEmbedder();
        return await KnowledgeBase.LoadAsync(contentRoot, embedder, CancellationToken.None);
    }

    private static void PrintPassages(IReadOnlyList<ScoredChunk> hits)
    {
        Console.WriteLine("Passages");
        if (hits.Count == 0)
        {
            Console.WriteLine("(none)");
            Console.WriteLine();
            return;
        }

        for (var i = 0; i < hits.Count; i++)
        {
            var hit = hits[i];
            Console.WriteLine($"{i + 1}. {hit.Chunk.Source} — {hit.Chunk.Heading}  (rrf {hit.Score:0.0000})");
            var excerpt = hit.Chunk.Text.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            if (excerpt.Length > 220)
            {
                excerpt = excerpt[..220] + "...";
            }

            Console.WriteLine("   " + excerpt);
        }

        Console.WriteLine();
    }

    private static int Help(int code)
    {
        Console.WriteLine("""
            Runbook Copilot — a small retrieval and tool-calling app for learning.

            Commands
              ask "<question>"       Retrieve runbook passages. Generate an answer only if a model is configured.
              eval                   Score whether the right runbook is in the top 3. No model required.
              agent --scripted "<q>" Walk the tool loop with a stand-in model.
              agent "<question>"     Same loop, with a configured chat model that supports tools.
              extract "<sentence>"   Turn a sentence into a tenant JSON object and validate it.
              trace ask "<q>"        Print the request body ask would post. Sends nothing.
              trace agent "<q>"      Print every request body the tool loop would post. Sends nothing.

            Examples
              dotnet run --project src/RunbookCopilot -- eval
              dotnet run --project src/RunbookCopilot -- ask "Which region is paired with eastus for failover?"
              dotnet run --project src/RunbookCopilot -- agent --scripted "Is billing-api healthy and where is failover documented?"
              dotnet run --project src/RunbookCopilot -- extract "Create a trial tenant for Contoso in eastus, environment dev, ending on 2026-11-01"
              dotnet run --project src/RunbookCopilot -- trace ask "Which region is paired with eastus for failover?"

            Model settings are environment variables. Leave them empty until you have an endpoint.
              COPILOT_PROVIDER          OpenAICompatible or AzureOpenAI
              COPILOT_ENDPOINT          http://localhost:11434/v1   or   https://YOUR-RESOURCE.openai.azure.com
              COPILOT_API_KEY           empty for a local Ollama, required for Azure
              COPILOT_CHAT_MODEL        llama3.2 or your Azure deployment name
              COPILOT_EMBEDDING_MODEL   optional. Empty keeps the local hashing embedder.
            """);
        return code;
    }
}
