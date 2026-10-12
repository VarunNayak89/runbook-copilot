// ============================================================================
// CopilotConfig.cs - settings, and where the API key comes from.
//
// THE SECURITY RULE THIS FILE EXISTS FOR:
// An API key must NEVER be written in a file that goes into git. Once a key is in
// git history it is effectively public forever, even if you delete it in a later commit.
//
// So: the file holds an EMPTY key, and the real key comes from an environment
// variable at run time. This is the same rule you already follow for pipeline secrets.
// ============================================================================

using System.Text.Json;

namespace RunbookCopilot;

public sealed class CopilotConfig
{
    public string Provider { get; set; } = "None";

    public string Endpoint { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public string ChatModel { get; set; } = "";

    public string EmbeddingModel { get; set; } = "";

    public string ApiVersion { get; set; } = "2024-10-21";

    public bool IsRemoteProvider =>
        Provider.Equals("OpenAICompatible", StringComparison.OrdinalIgnoreCase)
        || Provider.Equals("AzureOpenAI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Do we have everything needed to call a real chat model?
    ///
    /// When this is false the app does NOT fail. It prints the retrieved passages and
    /// stops. That is a deliberate design choice: the offline mode is honest and still
    /// teaches you the important half. An app that crashes without a key would be
    /// useless for learning.
    /// </summary>
    public bool UseRemoteChat =>
        IsRemoteProvider
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ChatModel);

    /// <summary>
    /// Chat and embeddings are checked SEPARATELY, because they are separate models.
    /// You can use a real chat model while keeping the offline hashing embedder.
    /// Keeping the embedder fixed means your eval scores stay comparable over time.
    /// </summary>
    public bool UseRemoteEmbeddings =>
        IsRemoteProvider
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(EmbeddingModel);

    /// <summary>
    /// Load settings from the file, then let environment variables override them.
    ///
    /// The order matters: file first, environment second, so the environment always wins.
    /// This is the standard layering you already use in ASP.NET configuration.
    /// </summary>
    public static CopilotConfig Load(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "appsettings.json");
        var config = File.Exists(path)
            ? JsonSerializer.Deserialize<CopilotConfig>(File.ReadAllText(path), JsonDefaults.Options) ?? new CopilotConfig()
            : new CopilotConfig();

        config.Provider = Override("COPILOT_PROVIDER", config.Provider);
        config.Endpoint = Override("COPILOT_ENDPOINT", config.Endpoint);
        config.ApiKey = Override("COPILOT_API_KEY", config.ApiKey);
        config.ChatModel = Override("COPILOT_CHAT_MODEL", config.ChatModel);
        config.EmbeddingModel = Override("COPILOT_EMBEDDING_MODEL", config.EmbeddingModel);
        config.ApiVersion = Override("COPILOT_API_VERSION", config.ApiVersion);
        return config;
    }

    /// <summary>
    /// Use the environment variable when it has a real value, otherwise keep the file value.
    ///
    /// IsNullOrWhiteSpace, not just null: an empty or blank variable is treated as
    /// "not set". Otherwise `$env:COPILOT_CHAT_MODEL = ""` would wipe your file setting
    /// and leave you debugging why the model stopped being used.
    /// </summary>
    private static string Override(string name, string current)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? current : value.Trim();
    }
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}
