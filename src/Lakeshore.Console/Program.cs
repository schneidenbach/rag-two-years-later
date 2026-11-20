// Lakeshore console host.
//   dotnet run -- chat [workorders|full]        interactive agent with a live tool-call trail
//   dotnet run -- ask "<question>" [full]        one-shot
//   dotnet run -- kb "<question>"                Azure AI Search agentic retrieval (streamed plan -> subqueries -> answer)
//   dotnet run -- naive-wo "<question>"          2024-style vector RAG over work orders
//   dotnet run -- naive-docs "<question>"        2024-style vector RAG over documents
//   dotnet run -- setup embed                    embed work orders (pgvector) for the naive baseline
//   dotnet run -- setup docs <folder>            upload docs to Blob, index, create knowledge source + knowledge base
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Identity;
using Lakeshore.Agents;
using Lakeshore.Agents.Knowledge;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

var config = LakeshoreConfig.Build();
var settings = LakeshoreSettings.From(config);
var lake = new LakeshoreAgents(settings, new AzureCliCredential());
var cmd = args.FirstOrDefault() ?? "chat";

switch (cmd)
{
    case "setup" when args.ElementAtOrDefault(1) == "embed":
        await new NaiveRag(lake).EmbedWorkOrdersAsync(n => Console.WriteLine($"embedded {n:N0}"));
        break;
    case "setup" when args.ElementAtOrDefault(1) == "docs":
        var ing = new DocumentIngestor(lake, config["Lakeshore:StorageAccount"] ?? throw new("Lakeshore:StorageAccount missing"));
        await ing.UploadFolderAsync(args.ElementAtOrDefault(2) ?? "docs-corpus", Console.WriteLine);
        await ing.IndexFromBlobAsync(Console.WriteLine);
        break;
    case "kb":
        await foreach (var (evt, data) in lake.KnowledgeBase.RetrieveStreamAsync(args[1]))
        {
            Ui.Dim($"[{evt}]");
            if (evt == "activity.completed" && data?["type"]?.GetValue<string>() == "searchIndex")
                Ui.Tool($"  subquery: {data["searchIndexArguments"]?["search"]}  ({data["count"]} docs)");
            if (evt == "answer.completed") Console.WriteLine(data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        break;
    case "naive-wo" or "naive-docs":
        var naive = new NaiveRag(lake);
        var (hits, answer) = cmd == "naive-wo" ? await naive.WorkOrdersAsync(args[1]) : await naive.DocumentsAsync(args[1]);
        foreach (var h in hits) Ui.Dim($"  retrieved: {h!["wo_number"] ?? h["doc_id"]}  {h["title"]}  {h["section"]}");
        await foreach (var t in answer) Console.Write(t);
        Console.WriteLine();
        break;
    case "ask":
        await RunAsync(lake.Create(Kind(args.ElementAtOrDefault(2))), args[1], null);
        break;
    default:
        var agent = lake.Create(Kind(args.ElementAtOrDefault(1)));
        var session = await agent.CreateSessionAsync();
        Ui.Dim($"Lakeshore {agent.Name} - {settings.ChatDeployment}. Ctrl+C to exit.");
        while (true)
        {
            Console.Write("\n> ");
            var q = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(q)) continue;
            await RunAsync(agent, q, session);
        }
}

static AgentKind Kind(string? s) => s?.ToLowerInvariant() == "full" ? AgentKind.Full : AgentKind.WorkOrders;

static async Task RunAsync(AIAgent agent, string question, AgentSession? session)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    int calls = 0;
    await foreach (var update in agent.RunStreamingAsync(question, session))
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    calls++;
                    Console.WriteLine();
                    Ui.Tool($"→ {call.Name} {JsonSerializer.Serialize(call.Arguments)}");
                    break;
                case FunctionResultContent result:
                    Ui.Dim($"  ← {Summarize(result.Result)}");
                    break;
                case TextContent text:
                    Console.Write(text.Text);
                    break;
            }
        }
    }
    Console.WriteLine();
    Ui.Dim($"[{calls} tool calls, {sw.Elapsed.TotalSeconds:F1}s]");
}

static string Summarize(object? result)
{
    var s = result?.ToString() ?? "";
    try
    {
        var node = JsonNode.Parse(s is ['"', ..] ? JsonSerializer.Deserialize<string>(s)! : s);
        return node?["kind"]?.GetValue<string>() switch
        {
            "aggregate" => $"{node["rows"]!.AsArray().Count} rows (population {node["population"]})",
            "work_order_list" => $"{node["rows"]!.AsArray().Count} work orders",
            "work_order_details" => $"{node["workOrders"]!.AsArray().Count} timelines",
            "knowledge_retrieval" => $"{node["subqueries"]!.AsArray().Count} subqueries, {node["references"]!.AsArray().Count} references",
            "escalation_draft" => $"DRAFT: {node["subject"]}",
            var k => k ?? s[..Math.Min(120, s.Length)],
        };
    }
    catch { return s[..Math.Min(160, s.Length)]; }
}

static class Ui
{
    public static void Tool(string s) { Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine(s); Console.ResetColor(); }
    public static void Dim(string s) { Console.ForegroundColor = ConsoleColor.DarkGray; Console.WriteLine(s); Console.ResetColor(); }
}
