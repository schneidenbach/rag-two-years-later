using Microsoft.Extensions.Configuration;

namespace Lakeshore.Agents;

public sealed record LakeshoreSettings
{
    public required string ProjectEndpoint { get; init; }        // https://<acct>.services.ai.azure.com/api/projects/<proj>
    public required string OpenAIEndpoint { get; init; }         // https://<acct>.openai.azure.com
    public string ChatDeployment { get; init; } = "gpt-5.5";
    public string MiniDeployment { get; init; } = "gpt-5.4-mini";
    public string EmbeddingDeployment { get; init; } = "text-embedding-3-small";
    public required string SearchEndpoint { get; init; }
    public string IndexName { get; init; } = "lakeshore-docs";
    public string KnowledgeSourceName { get; init; } = "lakeshore-ks";
    public string KnowledgeBaseName { get; init; } = "lakeshore-kb";
    public required string PostgresConnection { get; init; }
    public string ReasoningEffort { get; init; } = "low";

    /// <summary>Reads LAKESHORE__* env vars / appsettings "Lakeshore" section. Foundry-injected vars are honored when hosted.</summary>
    public static LakeshoreSettings From(IConfiguration config)
    {
        var s = config.GetSection("Lakeshore");
        string Req(string key, params string[] fallbacks) =>
            s[key] ?? fallbacks.Select(f => config[f]).FirstOrDefault(v => !string.IsNullOrEmpty(v))
            ?? throw new InvalidOperationException($"Missing configuration Lakeshore:{key}");
        return new LakeshoreSettings
        {
            ProjectEndpoint = Req(nameof(ProjectEndpoint), "FOUNDRY_PROJECT_ENDPOINT"),
            OpenAIEndpoint = Req(nameof(OpenAIEndpoint)),
            ChatDeployment = s[nameof(ChatDeployment)] ?? config["AZURE_AI_MODEL_DEPLOYMENT_NAME"] ?? "gpt-5.5",
            MiniDeployment = s[nameof(MiniDeployment)] ?? "gpt-5.4-mini",
            EmbeddingDeployment = s[nameof(EmbeddingDeployment)] ?? "text-embedding-3-small",
            SearchEndpoint = Req(nameof(SearchEndpoint)),
            IndexName = s[nameof(IndexName)] ?? "lakeshore-docs",
            KnowledgeSourceName = s[nameof(KnowledgeSourceName)] ?? "lakeshore-ks",
            KnowledgeBaseName = s[nameof(KnowledgeBaseName)] ?? "lakeshore-kb",
            PostgresConnection = Req(nameof(PostgresConnection)),
            ReasoningEffort = s[nameof(ReasoningEffort)] ?? "low",
        };
    }
}
