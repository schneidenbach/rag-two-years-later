using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;

namespace Lakeshore.Agents.Knowledge;

/// <summary>
/// Thin REST client for Azure AI Search agentic retrieval (api-version 2026-10-01 GA):
/// index, knowledge source, knowledge base, retrieve (+SSE), and a plain vector query for the naive baseline.
/// </summary>
public sealed class KnowledgeBaseClient
{
    public const string ApiVersion = "2026-10-01";
    readonly HttpClient _http;
    readonly TokenCredential _cred;
    readonly LakeshoreSettings _s;
    AccessToken _token;

    public KnowledgeBaseClient(LakeshoreSettings settings, TokenCredential credential, HttpClient? http = null)
    {
        _s = settings;
        _cred = credential;
        _http = http ?? new HttpClient();
        _http.BaseAddress = new Uri(settings.SearchEndpoint.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromMinutes(3);
    }

    async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        if (_token.ExpiresOn < DateTimeOffset.UtcNow.AddMinutes(2))
            _token = await _cred.GetTokenAsync(new TokenRequestContext(["https://search.azure.com/.default"]), ct);
        var req = new HttpRequestMessage(method, $"{path}{(path.Contains('?') ? '&' : '?')}api-version={ApiVersion}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.Token);
        if (body is not null) req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return req;
    }

    async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct, bool preferRepresentation = false)
    {
        using var req = await RequestAsync(method, path, body, ct);
        if (preferRepresentation) req.Headers.Add("Prefer", "return=representation");
        using var res = await _http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"{method} {path} -> {(int)res.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    // ---------------- setup ----------------
    public Task CreateOrUpdateIndexAsync(int dimensions = 1536, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"indexes('{_s.IndexName}')", new JsonObject
        {
            ["name"] = _s.IndexName,
            ["fields"] = new JsonArray(
                F("id", "Edm.String", key: true, filterable: true),
                F("doc_id", "Edm.String", filterable: true),
                F("title", "Edm.String", searchable: true),
                F("section", "Edm.String", searchable: true),
                F("doc_type", "Edm.String", filterable: true, facetable: true),
                F("effective_date", "Edm.String", filterable: true),
                F("source_url", "Edm.String"),
                F("content", "Edm.String", searchable: true),
                new JsonObject
                {
                    ["name"] = "content_vector", ["type"] = "Collection(Edm.Single)", ["searchable"] = true, ["stored"] = false,
                    ["dimensions"] = dimensions, ["vectorSearchProfile"] = "hnsw-te3",
                }),
            ["semantic"] = new JsonObject
            {
                ["defaultConfiguration"] = "semantic-config",
                ["configurations"] = new JsonArray(new JsonObject
                {
                    ["name"] = "semantic-config",
                    ["prioritizedFields"] = new JsonObject
                    {
                        ["titleField"] = new JsonObject { ["fieldName"] = "title" },
                        ["prioritizedContentFields"] = new JsonArray(new JsonObject { ["fieldName"] = "content" }),
                        ["prioritizedKeywordsFields"] = new JsonArray(new JsonObject { ["fieldName"] = "section" }),
                    },
                }),
            },
            ["vectorSearch"] = new JsonObject
            {
                ["algorithms"] = new JsonArray(new JsonObject { ["name"] = "hnsw", ["kind"] = "hnsw" }),
                ["profiles"] = new JsonArray(new JsonObject { ["name"] = "hnsw-te3", ["algorithm"] = "hnsw", ["vectorizer"] = "aoai-te3" }),
                ["vectorizers"] = new JsonArray(new JsonObject
                {
                    ["name"] = "aoai-te3", ["kind"] = "azureOpenAI",
                    ["azureOpenAIParameters"] = new JsonObject
                    {
                        ["resourceUri"] = _s.OpenAIEndpoint, ["deploymentId"] = _s.EmbeddingDeployment, ["modelName"] = "text-embedding-3-small",
                    },
                }),
            },
        }, ct, preferRepresentation: true);

    static JsonObject F(string name, string type, bool key = false, bool searchable = false, bool filterable = false, bool facetable = false) => new()
    {
        ["name"] = name, ["type"] = type, ["key"] = key, ["searchable"] = searchable, ["filterable"] = filterable, ["facetable"] = facetable,
    };

    public async Task UploadAsync(IEnumerable<JsonObject> docs, CancellationToken ct = default)
    {
        foreach (var batch in docs.Chunk(100))
        {
            var value = new JsonArray(batch.Select(d => { d["@search.action"] = "mergeOrUpload"; return (JsonNode)d; }).ToArray());
            await SendAsync(HttpMethod.Post, $"indexes('{_s.IndexName}')/docs/search.index", new JsonObject { ["value"] = value }, ct);
        }
    }

    public Task CreateOrUpdateKnowledgeSourceAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"knowledgesources('{_s.KnowledgeSourceName}')", new JsonObject
        {
            ["name"] = _s.KnowledgeSourceName,
            ["kind"] = "searchIndex",
            ["description"] = "Lakeshore Health facilities knowledge: vendor contracts and SLAs, SOPs, policies, equipment manuals, finance memos, meeting minutes.",
            ["searchIndexParameters"] = new JsonObject
            {
                ["searchIndexName"] = _s.IndexName,
                ["semanticConfigurationName"] = "semantic-config",
                ["sourceDataFields"] = new JsonArray(
                    new JsonObject { ["name"] = "id" }, new JsonObject { ["name"] = "doc_id" }, new JsonObject { ["name"] = "title" },
                    new JsonObject { ["name"] = "section" }, new JsonObject { ["name"] = "doc_type" }, new JsonObject { ["name"] = "content" }),
            },
        }, ct, preferRepresentation: true);

    public Task CreateOrUpdateKnowledgeBaseAsync(CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"knowledgebases('{_s.KnowledgeBaseName}')", new JsonObject
        {
            ["name"] = _s.KnowledgeBaseName,
            ["description"] = "Facilities knowledge for Lakeshore Health",
            ["knowledgeSources"] = new JsonArray(new JsonObject { ["name"] = _s.KnowledgeSourceName }),
            ["models"] = new JsonArray(new JsonObject
            {
                ["kind"] = "azureOpenAI",
                ["azureOpenAIParameters"] = new JsonObject
                {
                    ["resourceUri"] = _s.OpenAIEndpoint, ["deploymentId"] = _s.MiniDeployment, ["modelName"] = "gpt-5.4-mini",
                },
            }),
            ["retrievalReasoningEffort"] = new JsonObject { ["kind"] = "medium" },
            ["outputMode"] = "answerSynthesis",
            ["retrievalInstructions"] = "Vendor contracts and exhibits are authoritative for vendor obligations and lead times. SOPs are authoritative for dispatch and routing. Meeting minutes and memos give history and context. Prefer the most recent revision.",
            ["answerInstructions"] = "Answer concisely for a facilities director. Cite every factual claim with [ref_id:N]. Quote clause numbers, SLA numbers, dates and dollar figures exactly. If documents conflict, say so.",
        }, ct, preferRepresentation: true);

    // ---------------- retrieval ----------------
    public JsonObject BuildRetrieveRequest(string question, string outputMode = "answerSynthesis", string effort = "medium") => new()
    {
        ["messages"] = new JsonArray(new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = question }),
        }),
        ["retrievalReasoningEffort"] = new JsonObject { ["kind"] = effort },
        ["outputMode"] = outputMode,
        ["includeActivity"] = true,
        ["maxRuntimeInSeconds"] = 90,
        ["maxOutputSizeInTokens"] = 12000,
        ["knowledgeSourceParams"] = new JsonArray(new JsonObject
        {
            ["knowledgeSourceName"] = _s.KnowledgeSourceName,
            ["kind"] = "searchIndex",
            ["alwaysQuerySource"] = true,
            ["includeReferences"] = true,
            ["includeReferenceSourceData"] = true,
            ["rerankerThreshold"] = 1.2,
        }),
    };

    public async Task<JsonNode> RetrieveAsync(string question, string outputMode = "answerSynthesis", string effort = "medium", CancellationToken ct = default) =>
        (await SendAsync(HttpMethod.Post, $"knowledgebases('{_s.KnowledgeBaseName}')/retrieve", BuildRetrieveRequest(question, outputMode, effort), ct))!;

    /// <summary>Streams SSE events (event name + JSON data) from the retrieve endpoint.</summary>
    public async IAsyncEnumerable<(string Event, JsonNode? Data)> RetrieveStreamAsync(
        string question, string outputMode = "answerSynthesis", string effort = "medium", [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var req = await RequestAsync(HttpMethod.Post, $"knowledgebases('{_s.KnowledgeBaseName}')/retrieve", BuildRetrieveRequest(question, outputMode, effort), ct);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"retrieve -> {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync(ct)}");
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync(ct));
        string? evt = null; var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith(':')) continue;                      // heartbeat
            if (line.Length == 0)
            {
                if (evt is not null || data.Length > 0)
                {
                    JsonNode? node = null;
                    try { node = data.Length > 0 ? JsonNode.Parse(data.ToString()) : null; } catch (JsonException) { node = JsonValue.Create(data.ToString()); }
                    yield return (evt ?? "message", node);
                }
                evt = null; data.Clear();
                continue;
            }
            if (line.StartsWith("event:")) evt = line[6..].Trim();
            else if (line.StartsWith("data:")) data.Append(line[5..].TrimStart());
        }
    }

    /// <summary>Naive 2024-style baseline: one vector query, top-k chunks, no planning, no reranking.</summary>
    public async Task<JsonArray> NaiveVectorSearchAsync(string question, int k = 5, CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Post, $"indexes('{_s.IndexName}')/docs/search.post.search", new JsonObject
        {
            ["select"] = "id,doc_id,title,section,content",
            ["top"] = k,
            ["vectorQueries"] = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = question, ["fields"] = "content_vector", ["k"] = k }),
        }, ct);
        return res!["value"]!.AsArray();
    }
}
