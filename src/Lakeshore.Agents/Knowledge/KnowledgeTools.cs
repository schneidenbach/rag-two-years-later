using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Lakeshore.Agents.Knowledge;

/// <summary>Agent tool over the Azure AI Search knowledge base (agentic retrieval).</summary>
public sealed class KnowledgeTools(KnowledgeBaseClient kb)
{
    public const string SearchToolName = "search_facilities_knowledge";

    public IEnumerable<AIFunction> AsAIFunctions() => [AIFunctionFactory.Create(SearchAsync, SearchToolName)];

    [Description("""
        Search Lakeshore Health's facilities document library (vendor contracts and SLAs, SOPs, policies, equipment manuals,
        finance memos, operations meeting minutes) using Azure AI Search agentic retrieval. The knowledge base plans its own
        subqueries, so ask one complete, self-contained question that includes the specifics you learned from the data
        (vendor names, dates, part names, SOP numbers). Returns cited passages; cite them as [doc_id §section].
        """)]
    public async Task<string> SearchAsync(
        [Description("A complete natural-language question with all relevant specifics.")] string question,
        CancellationToken ct = default)
    {
        var res = await kb.RetrieveAsync(question, outputMode: "extractiveData", effort: "low", ct: ct);
        return Shape(res, includeAnswer: false).ToJsonString();
    }

    /// <summary>Normalizes a retrieve response into the shape the UI renders: plan -> subqueries -> references -> answer.</summary>
    public static JsonObject Shape(JsonNode res, bool includeAnswer)
    {
        var activity = res["activity"]?.AsArray() ?? [];
        var subqueries = new JsonArray();
        JsonObject? plan = null;
        foreach (var a in activity)
        {
            var type = a?["type"]?.GetValue<string>();
            if (type == "modelQueryPlanning")
                plan = new JsonObject { ["elapsedMs"] = a!["elapsedMs"]?.DeepClone(), ["inputTokens"] = a["inputTokens"]?.DeepClone(), ["outputTokens"] = a["outputTokens"]?.DeepClone() };
            else if (type == "searchIndex")
                subqueries.Add(new JsonObject
                {
                    ["id"] = a!["id"]?.DeepClone(),
                    ["search"] = a["searchIndexArguments"]?["search"]?.DeepClone(),
                    ["count"] = a["count"]?.DeepClone(),
                    ["elapsedMs"] = a["elapsedMs"]?.DeepClone(),
                });
        }
        var refs = new JsonArray();
        foreach (var r in res["references"]?.AsArray() ?? [])
        {
            var sd = r?["sourceData"];
            refs.Add(new JsonObject
            {
                ["ref_id"] = r!["id"]?.DeepClone(),
                ["subquery"] = r["activitySource"]?.DeepClone(),
                ["doc_id"] = sd?["doc_id"]?.DeepClone(),
                ["title"] = sd?["title"]?.DeepClone(),
                ["section"] = sd?["section"]?.DeepClone(),
                ["doc_type"] = sd?["doc_type"]?.DeepClone(),
                ["rerankerScore"] = r["rerankerScore"]?.DeepClone(),
                ["content"] = sd?["content"]?.DeepClone(),
            });
        }
        var shaped = new JsonObject
        {
            ["kind"] = "knowledge_retrieval",
            ["plan"] = plan,
            ["subqueries"] = subqueries,
            ["references"] = refs,
        };
        if (includeAnswer) shaped["answer"] = res["response"]?[0]?["content"]?[0]?["text"]?.DeepClone();
        return shaped;
    }
}
