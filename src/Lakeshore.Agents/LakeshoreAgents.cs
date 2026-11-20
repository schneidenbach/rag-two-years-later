using System.ClientModel.Primitives;
using Azure.AI.Projects;
using Azure.Core;
using Lakeshore.Agents.Knowledge;
using Lakeshore.Agents.WorkOrders;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Npgsql;
using OpenAI;

namespace Lakeshore.Agents;

public enum AgentKind
{
    /// <summary>Demo 1: macro -> micro over the work-order database.</summary>
    WorkOrders,
    /// <summary>Demo 2 payoff: work-order data + facilities knowledge base + draft action.</summary>
    Full,
}

/// <summary>Builds the Lakeshore agents and the clients they share. One instance per process.</summary>
public sealed class LakeshoreAgents
{
    public LakeshoreSettings Settings { get; }
    public TokenCredential Credential { get; }
    public NpgsqlDataSource Db { get; }
    public KnowledgeBaseClient KnowledgeBase { get; }
    public WorkOrderTools WorkOrderTools { get; }
    public KnowledgeTools KnowledgeTools { get; }
    public ActionTools ActionTools { get; } = new();
    public OpenAIClient OpenAI { get; }
    readonly AIProjectClient _project;

    public LakeshoreAgents(LakeshoreSettings settings, TokenCredential credential)
    {
        Settings = settings;
        Credential = credential;
        var dsb = new NpgsqlDataSourceBuilder(settings.PostgresConnection);
        Db = dsb.Build();
        KnowledgeBase = new KnowledgeBaseClient(settings, credential);
        WorkOrderTools = new WorkOrderTools(Db);
        KnowledgeTools = new KnowledgeTools(KnowledgeBase);
        _project = new AIProjectClient(new Uri(settings.ProjectEndpoint), credential);
        // Azure OpenAI v1 endpoint with Entra auth (plain OpenAI SDK; avoids the Azure.AI.OpenAI version skew).
        OpenAI = new OpenAIClient(
            new BearerTokenPolicy(credential, "https://cognitiveservices.azure.com/.default"),
            new OpenAIClientOptions { Endpoint = new Uri(settings.OpenAIEndpoint.TrimEnd('/') + "/openai/v1/") });
    }

    public IChatClient MiniChat => OpenAI.GetChatClient(Settings.MiniDeployment).AsIChatClient();
    public IEmbeddingGenerator<string, Embedding<float>> Embeddings => OpenAI.GetEmbeddingClient(Settings.EmbeddingDeployment).AsIEmbeddingGenerator();

    public AIAgent Create(AgentKind kind)
    {
        var tools = new List<AITool>(WorkOrderTools.AsAIFunctions());
        if (kind == AgentKind.Full) tools.AddRange(KnowledgeTools.AsAIFunctions());
        tools.AddRange(ActionTools.AsAIFunctions());

        return _project.AsAIAgent(new ChatClientAgentOptions
        {
            Name = kind == AgentKind.Full ? "lakeshore-facilities-agent" : "lakeshore-workorder-analyst",
            Description = "Lakeshore Health facilities analyst: investigates work-order performance from macro trends down to individual work orders.",
            ChatOptions = new ChatOptions
            {
                ModelId = Settings.ChatDeployment,
                Instructions = kind == AgentKind.Full ? Prompts.Full : Prompts.WorkOrders,
                Tools = tools,
                Reasoning = new ReasoningOptions { Effort = Settings.ReasoningEffort.ToLowerInvariant() switch { "medium" => ReasoningEffort.Medium, "high" => ReasoningEffort.High, _ => ReasoningEffort.Low } },
            },
        });
    }
}

public static class Prompts
{
    const string Context = """
        You are the facilities operations analyst for Lakeshore Health, a three-campus hospital system
        (North Campus, South Campus, Riverside Campus). Today is 2026-11-16. Work-order data covers 2025-06-01 through 2026-11-15.
        """;

    const string Method = """
        ## How you investigate (macro -> micro)
        1. Establish the trend. Use query_work_order_data with timeSeries (Month) and compare against a baseline: other categories
           or the prior period. Quantify the change.
        2. Segment. Break the affected population down by Campus, then Building, Priority, Subcategory, AfterHours, PartsVendor.
           Follow the largest variance. When a segment looks like the culprit, break it down one level further: a campus-level
           hotspot is often a single building. Use minimumGroupCount 10 to avoid noise.
        3. Decompose the cycle time. Compare AvgDaysToFirstResponse, AvgDaysWaitingOnParts, PartsOrderedRate and ReassignmentRate
           between the hotspot and everywhere else, and before vs. after the change. This shows WHERE the time goes.
        4. Drill to individual work orders. Use find_work_orders on the worst cases, then get_work_order_details on 2-3 of them, and read
           the action notes. Use actionNotesContain to check how widespread a pattern in the notes is.
        5. Test competing explanations before concluding. Example: if one technician looks slow, compare their labor hours
           and the mix of work they're assigned. Say explicitly what you ruled out.
        Look for more than one contributing cause. Be precise with numbers. Never invent data. Every claim needs a tool result behind it.

        ## Narration (a live audience is watching)
        Before EVERY tool call, write one short sentence saying what you're checking and why, e.g. "Checking whether
        North Campus's slowdown is campus-wide or concentrated in one building." Keep it under 20 words. No preamble.
        """;

    const string Output = """
        ## Final answer format (markdown, tight)
        **Finding:** one or two sentences.
        **Evidence:** 3-6 bullets with specific numbers and work order numbers (e.g. WO-2026-008123).
        **Ruled out:** bullets naming the hypotheses you disproved and why.
        **Recommended next step:** one concrete action, then call draft_escalation to prepare it for human review (never "send").
        """;

    public const string WorkOrders = Context + "\n" + Method + "\n" + Output;

    public const string Full = Context + "\n" + Method + """

        ## Documents
        You also have search_facilities_knowledge, which searches contracts, SLAs, SOPs, policies, manuals, memos and meeting minutes.
        Once the data shows WHAT is happening, use the documents to explain WHY: which contract clause, SOP revision or decision allows or causes it.
        Ask the knowledge base specific questions that include what you learned (vendor, part names, dates, SOP numbers).
        Cite documents as [doc_id §section] and connect them to the data (e.g., "contract allows 10 business days; actual average is 14.9 days").
        If remedies exist in a contract, quantify whether they've been triggered and include them in the escalation.
        """ + "\n" + Output;
}
