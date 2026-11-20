// Stage API: runs agents / baselines and streams events to the React UI over SSE.
// Every live run is recorded to recordings/; any recording can be replayed with original timing (stage safety net).
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Azure.Identity;
using Lakeshore.Agents;
using Lakeshore.Agents.Knowledge;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddLakeshore();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
var settings = LakeshoreSettings.From(builder.Configuration);
var lake = new LakeshoreAgents(settings, new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeManagedIdentityCredential = true }));
var naive = new NaiveRag(lake);
var recordingsDir = Path.GetFullPath(Path.Combine(FindRepoRoot(), "recordings"));
Directory.CreateDirectory(Path.Combine(recordingsDir, "pinned"));

var app = builder.Build();
app.UseCors();

var sessions = new ConcurrentDictionary<string, (AIAgent Agent, AgentSession Session)>();
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/api/health", () => Results.Ok(new { ok = true, model = settings.ChatDeployment }));

app.MapPost("/api/run", async (RunRequest req, HttpContext http, CancellationToken ct) =>
{
    var events = Channel.CreateUnbounded<JsonObject>();
    var recording = new List<JsonObject>();
    var sw = System.Diagnostics.Stopwatch.StartNew();
    void Emit(JsonObject e) { e["t"] = sw.ElapsedMilliseconds; recording.Add(e); events.Writer.TryWrite(e); }

    var producer = Task.Run(async () =>
    {
        try
        {
            Emit(new JsonObject { ["type"] = "start", ["mode"] = req.Mode, ["question"] = req.Question });
            switch (req.Mode)
            {
                case "agent-workorders" or "agent-full":
                    await RunAgentAsync(req, Emit, ct);
                    break;
                case "naive-wo" or "naive-docs":
                    var (hits, answer) = req.Mode == "naive-wo" ? await naive.WorkOrdersAsync(req.Question, ct: ct) : await naive.DocumentsAsync(req.Question, ct: ct);
                    Emit(new JsonObject { ["type"] = "retrieved", ["items"] = hits });
                    await foreach (var t in answer.WithCancellation(ct)) Emit(new JsonObject { ["type"] = "text", ["delta"] = t });
                    break;
                case "kb":
                    await foreach (var (evt, data) in lake.KnowledgeBase.RetrieveStreamAsync(req.Question, ct: ct))
                        Emit(new JsonObject { ["type"] = "kb", ["event"] = evt, ["data"] = data?.DeepClone() });
                    break;
                default:
                    throw new ArgumentException($"Unknown mode {req.Mode}");
            }
            Emit(new JsonObject { ["type"] = "done", ["elapsedMs"] = sw.ElapsedMilliseconds });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Emit(new JsonObject { ["type"] = "error", ["message"] = ex.Message });
        }
        finally
        {
            events.Writer.TryComplete();
            if (recording.Count > 2)
            {
                var file = Path.Combine(recordingsDir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{req.Mode}.json");
                await File.WriteAllTextAsync(file, new JsonObject
                {
                    ["mode"] = req.Mode, ["question"] = req.Question, ["recordedAt"] = DateTime.Now.ToString("s"),
                    ["ok"] = recording[^1]["type"]?.GetValue<string>() == "done",
                    ["events"] = new JsonArray(recording.Select(e => (JsonNode)e.DeepClone()).ToArray()),
                }.ToJsonString(), CancellationToken.None);
            }
        }
    }, ct);

    await StreamSse(http, events.Reader.ReadAllAsync(ct), ct);
    await producer;
});

// Replay: streams a recording with its original timing (speed multiplier optional).
app.MapPost("/api/replay", async (ReplayRequest req, HttpContext http, CancellationToken ct) =>
{
    var path = ResolveRecording(req.File, req.Mode);
    if (path is null) { http.Response.StatusCode = 404; return; }
    var rec = JsonNode.Parse(await File.ReadAllTextAsync(path, ct))!;
    var speed = req.Speed is > 0 ? req.Speed.Value : 1.0;
    await StreamSse(http, Replay(rec["events"]!.AsArray(), speed, ct), ct);
});

app.MapGet("/api/recordings", () =>
{
    var list = Directory.GetFiles(recordingsDir, "*.json", SearchOption.AllDirectories)
        .Select(f =>
        {
            var n = JsonNode.Parse(File.ReadAllText(f))!;
            return new
            {
                file = Path.GetRelativePath(recordingsDir, f),
                mode = n["mode"]?.GetValue<string>(),
                question = n["question"]?.GetValue<string>(),
                ok = n["ok"]?.GetValue<bool>() ?? false,
                pinned = f.Contains($"{Path.DirectorySeparatorChar}pinned{Path.DirectorySeparatorChar}"),
                durationMs = n["events"]?.AsArray().LastOrDefault()?["t"]?.GetValue<long>(),
            };
        })
        .OrderByDescending(r => r.pinned).ThenByDescending(r => r.file);
    return Results.Ok(list);
});

// Pin a recording as the known-good fallback for its mode (recordings/pinned/<mode>.json).
app.MapPost("/api/recordings/pin", (PinRequest req) =>
{
    var src = Path.GetFullPath(Path.Combine(recordingsDir, req.File));
    if (!src.StartsWith(recordingsDir) || !File.Exists(src)) return Results.NotFound();
    var mode = JsonNode.Parse(File.ReadAllText(src))!["mode"]!.GetValue<string>();
    var slot = string.IsNullOrWhiteSpace(req.Slot) ? mode : req.Slot;
    File.Copy(src, Path.Combine(recordingsDir, "pinned", $"{slot}.json"), overwrite: true);
    return Results.Ok(new { pinned = $"pinned/{slot}.json" });
});

app.Run();

async Task RunAgentAsync(RunRequest req, Action<JsonObject> emit, CancellationToken ct)
{
    var kind = req.Mode == "agent-full" ? AgentKind.Full : AgentKind.WorkOrders;
    var key = $"{req.SessionId ?? Guid.NewGuid().ToString()}:{kind}";
    var (agent, session) = sessions.TryGetValue(key, out var existing)
        ? existing
        : await NewSessionAsync(kind);
    sessions[key] = (agent, session);

    var names = new Dictionary<string, string>();
    await foreach (var update in agent.RunStreamingAsync(req.Question, session, cancellationToken: ct))
    {
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case FunctionCallContent call:
                    names[call.CallId] = call.Name;
                    emit(new JsonObject
                    {
                        ["type"] = "tool_call", ["callId"] = call.CallId, ["name"] = call.Name,
                        ["args"] = JsonSerializer.SerializeToNode(call.Arguments, json),
                    });
                    break;
                case FunctionResultContent result:
                    emit(new JsonObject
                    {
                        ["type"] = "tool_result", ["callId"] = result.CallId, ["name"] = names.GetValueOrDefault(result.CallId),
                        ["result"] = ParseResult(result.Result),
                    });
                    break;
                case TextContent text when !string.IsNullOrEmpty(text.Text):
                    emit(new JsonObject { ["type"] = "text", ["delta"] = text.Text });
                    break;
            }
        }
    }
}

async Task<(AIAgent, AgentSession)> NewSessionAsync(AgentKind kind)
{
    var agent = lake.Create(kind);
    return (agent, await agent.CreateSessionAsync());
}

JsonNode? ParseResult(object? result)
{
    var s = result switch { JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText(), null => null, var o => o.ToString() };
    if (s is null) return null;
    try { return JsonNode.Parse(s); } catch { return JsonValue.Create(s); }
}

static async IAsyncEnumerable<JsonObject> Replay(JsonArray events, double speed, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
{
    long last = 0;
    foreach (var e in events.OfType<JsonObject>())
    {
        var t = e["t"]?.GetValue<long>() ?? last;
        var wait = (int)Math.Min(4000, (t - last) / speed);   // cap long model pauses so replay stays snappy
        if (wait > 0) await Task.Delay(wait, ct);
        last = t;
        var copy = (JsonObject)e.DeepClone();
        copy["replay"] = true;
        yield return copy;
    }
}

static async Task StreamSse(HttpContext http, IAsyncEnumerable<JsonObject> events, CancellationToken ct)
{
    http.Response.Headers.ContentType = "text/event-stream";
    http.Response.Headers.CacheControl = "no-cache";
    http.Response.Headers["X-Accel-Buffering"] = "no";
    await foreach (var e in events.WithCancellation(ct))
    {
        await http.Response.WriteAsync($"data: {e.ToJsonString()}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}

string? ResolveRecording(string? file, string? mode)
{
    var rel = file ?? (mode is null ? null : Path.Combine("pinned", $"{mode}.json"));
    if (rel is null) return null;
    var full = Path.GetFullPath(Path.Combine(recordingsDir, rel));
    return full.StartsWith(recordingsDir) && File.Exists(full) ? full : null;
}

static string FindRepoRoot()
{
    for (var d = new DirectoryInfo(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "Lakeshore.slnx"))) return d.FullName;
    return Directory.GetCurrentDirectory();
}

record RunRequest(string Mode, string Question, string? SessionId);
record ReplayRequest(string? File, string? Mode, double? Speed);
record PinRequest(string File, string? Slot);
