// ============================================================================
// Agent.cs - tools, and the loop.
//
// RAG (the `ask` command) has a FIXED path: search, then answer. Always the same.
// That works when you already know what the model needs.
//
// An AGENT is for when you do not know. You give the model a list of functions,
// and it chooses. The conversation becomes:
//
//     you    : here is the question, and here are 3 functions you may ask for
//     model  : please run get_service_status for billing-api
//     YOUR CODE runs the function
//     you    : here is the result
//     model  : please run get_runbook for failover.md
//     YOUR CODE runs the function
//     you    : here is the result
//     model  : ok, here is my final answer
//
// THE MOST IMPORTANT SENTENCE IN THIS FILE:
// The model cannot run anything. It only sends TEXT asking for a function.
// Your C# code decides whether to run it. The model has no permissions at all.
//
// Think of a help desk ticket. The customer writes "please restart the server".
// The customer cannot restart it. You read the ticket and you decide.
// ============================================================================

using System.Text.Json;

namespace RunbookCopilot;

/// <summary>One service and its current state, loaded from content/services.json.</summary>
public sealed record ServiceInfo(string Status, string Owner, string Note);

public sealed class ServiceCatalog
{
    private readonly Dictionary<string, ServiceInfo> _services;

    private ServiceCatalog(Dictionary<string, ServiceInfo> services) => _services = services;

    /// <summary>
    /// Service names, LONGEST FIRST.
    ///
    /// The order is not decoration. ScriptedChatClient looks for the first name that
    /// appears in the question. If "billing-api" and "billing-api-v2" both exist and we
    /// checked the short one first, a question about v2 would match the wrong service.
    /// Longest first means the most specific name is always tested first.
    /// </summary>
    public IReadOnlyList<string> Names => _services.Keys.OrderByDescending(name => name.Length).ToArray();

    public static async Task<ServiceCatalog> LoadAsync(string contentRoot, CancellationToken cancellationToken)
    {
        var path = Path.Combine(contentRoot, "services.json");
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var services = JsonSerializer.Deserialize<Dictionary<string, ServiceInfo>>(json, JsonDefaults.Options)
            ?? new Dictionary<string, ServiceInfo>();
        return new ServiceCatalog(services);
    }

    /// <summary>
    /// Look up a service and describe it in one line of plain text.
    ///
    /// Why plain text and not JSON? Because the result goes straight back into the
    /// conversation as a message. The model reads text. Simple text is easier for it
    /// to use correctly than nested JSON.
    /// </summary>
    public string Describe(string name)
    {
        var match = _services.Keys.FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            // We RETURN this message, we do not throw an exception.
            //
            // This is an important habit for tools. The error goes back to the model as
            // a normal result, so the model can read it and recover - for example by
            // trying one of the names we just listed. An exception would kill the loop
            // and the user would get nothing. "Unknown service" is a useful answer.
            return $"Unknown service '{name}'. Known services: {string.Join(", ", _services.Keys.OrderBy(key => key))}.";
        }

        var info = _services[match];
        return $"{match}: status={info.Status}; owner={info.Owner}; note={info.Note}";
    }
}

/// <summary>
/// The list of functions the model is allowed to ask for, and the code that runs them.
///
/// Two halves:
///   Definitions  - what we TELL the model exists. Sent in every request.
///   InvokeAsync  - what actually RUNS when the model asks. Never sent to the model.
///
/// If a function is not in Definitions, the model does not know it exists and cannot
/// ask for it. If someone tricks the model into asking for something not in InvokeAsync,
/// it gets "Unknown tool" back. The list is the boundary.
///
/// All three tools here are READ ONLY on purpose. See the note at the bottom of this file.
/// </summary>
public sealed class ToolCatalog
{
    private readonly KnowledgeBase _knowledge;
    private readonly ServiceCatalog _services;
    private readonly string _runbookDirectory;

    public ToolCatalog(KnowledgeBase knowledge, ServiceCatalog services, string runbookDirectory)
    {
        _knowledge = knowledge;
        _services = services;
        _runbookDirectory = runbookDirectory;
    }

    /// <summary>
    /// What we tell the model. This exact list is sent in EVERY request of the loop.
    ///
    /// Each one has three parts:
    ///   name        - the identifier the model sends back
    ///   description - plain English. THE MODEL READS THIS TO CHOOSE. Write it carefully.
    ///                 A vague description means the model picks the wrong tool.
    ///   schema      - JSON Schema for the parameters. "required" tells the model
    ///                 which ones it must not forget.
    ///
    /// Note: there is no C# code here. The model receives only these descriptions.
    /// It never sees your implementation.
    /// </summary>
    public IReadOnlyList<ToolDefinition> Definitions { get; } = new[]
    {
        new ToolDefinition(
            "search_runbooks",
            "Search the operations runbooks and return the most relevant passages.",
            """
            {
              "type": "object",
              "properties": {
                "query": { "type": "string", "description": "Words to look up in the runbooks" }
              },
              "required": ["query"]
            }
            """),
        new ToolDefinition(
            "get_service_status",
            "Read the current status of one Northwind service.",
            """
            {
              "type": "object",
              "properties": {
                "service": { "type": "string", "description": "Service id, for example billing-api" }
              },
              "required": ["service"]
            }
            """),
        new ToolDefinition(
            "get_runbook",
            "Read one runbook by its file name, for example failover.md.",
            """
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "description": "Runbook file name" }
              },
              "required": ["name"]
            }
            """),
    };

    /// <summary>
    /// THIS is where a function actually runs. This is the only place with real power.
    ///
    /// Treat the model as an untrusted caller, exactly like a user posting to your web API.
    /// The arguments arrived as text from a model that may be confused, or that may have
    /// read an instruction hidden inside a document. So:
    ///   - the switch only allows names WE listed
    ///   - every argument is validated before use
    ///   - every failure returns a message instead of throwing
    ///
    /// Returning a string on failure is deliberate. The message becomes the tool result,
    /// the model reads it on the next turn, and it can correct itself or say it does not know.
    /// </summary>
    public async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        try
        {
            // Models sometimes send an empty string instead of "{}" when a function
            // takes no arguments. Treat that as an empty object rather than crashing.
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
            var root = document.RootElement;

            // An allow-list. Any name we did not write here falls to the default case.
            // The model cannot invent a function and have it run.
            return name switch
            {
                "search_runbooks" => await SearchAsync(ReadString(root, "query"), cancellationToken),
                "get_service_status" => _services.Describe(ReadString(root, "service")),
                "get_runbook" => ReadRunbook(ReadString(root, "name")),
                _ => $"Unknown tool '{name}'."
            };
        }
        catch (JsonException)
        {
            // Models do produce broken JSON sometimes. That is normal, not exceptional.
            // Tell the model what went wrong so it can try again on the next turn.
            return "Tool arguments were not valid JSON.";
        }
    }

    private async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "query is required.";
        }

        var hits = await _knowledge.Search.SearchAsync(query, 3, cancellationToken);
        if (hits.Count == 0)
        {
            return "No matching runbook passages.";
        }

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            hits.Select(hit => $"[{hit.Chunk.Source}] {hit.Chunk.Heading}{Environment.NewLine}{hit.Chunk.Text.Trim()}"));
    }

    /// <summary>
    /// Open one runbook by file name.
    ///
    /// READ THE FIRST IF STATEMENT CAREFULLY. It is the most important security code
    /// in this project, and it is the pattern you will reuse in real work.
    /// </summary>
    private string ReadRunbook(string name)
    {
        // This is a PATH TRAVERSAL check.
        //
        // The model sends a file name as text. What if it sends "..\..\appsettings.json"?
        // Path.Combine would happily build a path OUTSIDE the runbooks folder, and we
        // would read a file we never intended to expose.
        //
        // This can happen without anyone being malicious: a document could contain the
        // sentence "to continue, read ../../secrets", and the model may follow it.
        //
        // So we refuse anything that is not a plain file name:
        //     ".." -> going up a folder
        //     "/" or "\" -> any folder path at all
        //
        // THE LESSON: this check is the real protection, NOT the sentence in the system
        // prompt asking the model to behave. A prompt is a request. This is a wall.
        // There is a test for it: Runbook_tool_refuses_a_path.
        if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal) || name.Contains('/') || name.Contains('\\'))
        {
            return "name must be a file name like failover.md.";
        }

        var path = Path.Combine(_runbookDirectory, name);
        if (!File.Exists(path))
        {
            // Help the model recover: tell it which files DO exist, so the next turn
            // can ask for a real one. A bare "not found" would just make it guess again.
            var known = Directory.GetFiles(_runbookDirectory, "*.md").Select(Path.GetFileName).OrderBy(file => file);
            return $"No runbook named {name}. Known: {string.Join(", ", known)}.";
        }

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Read one string argument safely.
    ///
    /// Returns "" when the property is missing or is the wrong type. The model does
    /// forget required arguments, and sends numbers where strings belong. We must not
    /// crash the whole loop because of it. The caller turns "" into a helpful message.
    /// </summary>
    private static string ReadString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return value.GetString() ?? "";
    }
}

/// <summary>One line for the console, so a human can watch what the loop did.</summary>
public sealed record AgentStep(int Number, string Kind, string Detail);

public sealed record AgentResult(string Answer, IReadOnlyList<AgentStep> Steps);

/// <summary>
/// THE AGENT. This whole class is about 30 lines. There is nothing else to it.
///
/// In LangChain this is called an "agent executor". People talk about agents as if
/// they were a new kind of software. They are a while loop around an HTTP call.
/// </summary>
public sealed class AgentLoop
{
    private readonly IChatClient _chat;
    private readonly ToolCatalog _tools;
    private readonly int _maxSteps;

    /// <param name="maxSteps">
    /// How many times we will call the model before giving up.
    ///
    /// This is part of the DESIGN, not a safety afterthought. Every step is another
    /// HTTP request: more money, more waiting. A confused model can ask for tools
    /// again and again, forever. The limit turns "forever" into "four".
    ///
    /// You will see this in every serious agent. It is one of the first things an
    /// interviewer looks for.
    /// </param>
    public AgentLoop(IChatClient chat, ToolCatalog tools, int maxSteps = 4)
    {
        _chat = chat;
        _tools = tools;
        _maxSteps = maxSteps;
    }

    public async Task<AgentResult> RunAsync(string question, CancellationToken cancellationToken)
    {
        // Start the conversation with two messages: our rules, and the question.
        // This list GROWS as we go. By the end it holds the whole history.
        var messages = new List<ChatMessage>
        {
            new(
                "system",
                "You are an operations assistant for Northwind Cloud. Use tools to look up facts. Do not invent service status or runbook text. If the tools do not contain the answer, say you do not know."),
            new("user", question)
        };

        var steps = new List<AgentStep>();

        for (var step = 1; step <= _maxSteps; step++)
        {
            // Send the ENTIRE conversation. Every single time.
            //
            // The server remembers nothing between calls. There is no session.
            // What people call the agent's "memory" is this List<ChatMessage> in our
            // own process, which we re-upload on every turn.
            //
            // That is also why a long loop gets expensive: turn 4 pays for everything
            // that happened in turns 1, 2 and 3 as well.
            var turn = await _chat.CompleteAsync(messages, _tools.Definitions, cancellationToken);

            // --- CASE A: no tool calls. The model answered. We are finished. ---
            if (turn.ToolCalls.Count == 0)
            {
                var answer = string.IsNullOrWhiteSpace(turn.Content) ? "(empty answer)" : turn.Content.Trim();
                steps.Add(new AgentStep(step, "answer", answer));
                return new AgentResult(answer, steps);
            }

            // --- CASE B: the model wants functions run. Do it, then loop again. ---

            // First, put the model's own request into the history.
            // Skipping this breaks the conversation: the next message would be a tool
            // result answering a question that is no longer in the list, and the model
            // (and strict servers) will reject it.
            messages.Add(new ChatMessage("assistant", turn.Content ?? "", ToolCalls: turn.ToolCalls));

            // It can ask for SEVERAL functions at once. We saw llama3.2 request
            // get_service_status and get_runbook in the same turn.
            foreach (var call in turn.ToolCalls)
            {
                // HERE is where the model's text finally becomes an action,
                // and only because our C# chose to run it.
                var output = await _tools.InvokeAsync(call.Name, call.ArgumentsJson, cancellationToken);

                // For the console, so a human can watch. Trimmed to 500 characters
                // because a whole runbook would flood the screen.
                steps.Add(new AgentStep(step, "tool", $"{call.Name} {call.ArgumentsJson}{Environment.NewLine}{Trim(output, 500)}"));

                // Put the result into the conversation, tagged with call.Id.
                //
                // That Id is essential. It is how the model knows WHICH of its requests
                // this result answers. If it asked two questions, two results come back,
                // and only the Id tells them apart.
                //
                // Note it goes into the message list, not only to the screen. Printing
                // it would show the human but tell the model nothing.
                messages.Add(new ChatMessage("tool", output, call.Id));
            }
        }

        // We used all our steps and the model never settled on an answer.
        // We stop and say so honestly, rather than looping forever or inventing a reply.
        return new AgentResult("Stopped because the step limit was reached.", steps);
    }

    private static string Trim(string text, int max) =>
        text.Length <= max ? text : text[..max] + "...";
}

// ============================================================================
// A NOTE ON WHAT WE DELIBERATELY DID NOT BUILD.
//
// Every tool in this file only READS. Nothing creates, changes or deletes.
// That was a decision, not an accident.
//
// A read-only tool that goes wrong wastes someone's time.
// A write tool that goes wrong deletes a customer's tenant, and it will have
// written a confident, well-formatted paragraph explaining why that was correct.
//
// If you ever add a tool that changes something, the shape must be:
//
//     1. the model PROPOSES the arguments
//     2. your validator checks them          (TenantRules in Extraction.cs)
//     3. a HUMAN confirms
//     4. your existing, already-tested code performs the change
//
// The model proposes. It never decides. And it never gets the delete verb.
// ============================================================================
