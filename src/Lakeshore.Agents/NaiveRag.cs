using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Npgsql;

namespace Lakeshore.Agents;

/// <summary>
/// The "2024 RAG" baselines: embed the question, take the top-k nearest chunks, stuff them into one prompt.
/// No planning, no aggregation, no follow-up. This is what the agent is contrasted against.
/// </summary>
public sealed class NaiveRag(LakeshoreAgents a)
{
    const string System = "You are a helpful assistant for Lakeshore Health facilities. Answer the user's question using ONLY the provided context. Be confident and concise.";

    public async Task<(JsonArray Retrieved, IAsyncEnumerable<string> Answer)> WorkOrdersAsync(string question, int k = 5, CancellationToken ct = default)
    {
        var qv = (await a.Embeddings.GenerateAsync([question], cancellationToken: ct))[0].Vector.ToArray();
        var vec = "[" + string.Join(",", qv.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";
        await using var cmd = a.Db.CreateCommand("""
            SELECT wo.wo_number, wo.title, wo.description, wo.category, wo.priority,
                   to_char(wo.opened_at AT TIME ZONE 'America/Chicago', 'YYYY-MM-DD') AS opened,
                   round((1 - (wo.embedding <=> @v::vector))::numeric, 3) AS similarity
            FROM work_orders wo
            WHERE wo.embedding IS NOT NULL
            ORDER BY wo.embedding <=> @v::vector
            LIMIT @k
            """);
        cmd.Parameters.AddWithValue("v", vec);
        cmd.Parameters.AddWithValue("k", k);
        var rows = new JsonArray();
        var context = new StringBuilder();
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                rows.Add(new JsonObject
                {
                    ["wo_number"] = r.GetString(0), ["title"] = r.GetString(1), ["description"] = r.GetString(2),
                    ["category"] = r.GetString(3), ["priority"] = r.GetString(4), ["opened"] = r.GetString(5), ["similarity"] = r.GetDecimal(6),
                });
                context.AppendLine($"[{r.GetString(0)}] {r.GetString(1)} - {r.GetString(2)}");
            }
        return (rows, Stream(question, context.ToString(), ct));
    }

    public async Task<(JsonArray Retrieved, IAsyncEnumerable<string> Answer)> DocumentsAsync(string question, int k = 5, CancellationToken ct = default)
    {
        var hits = await a.KnowledgeBase.NaiveVectorSearchAsync(question, k, ct);
        var context = new StringBuilder();
        var rows = new JsonArray();
        foreach (var h in hits)
        {
            rows.Add(new JsonObject
            {
                ["id"] = h!["id"]?.DeepClone(), ["doc_id"] = h["doc_id"]?.DeepClone(), ["title"] = h["title"]?.DeepClone(),
                ["section"] = h["section"]?.DeepClone(), ["score"] = h["@search.score"]?.DeepClone(), ["content"] = h["content"]?.DeepClone(),
            });
            context.AppendLine($"[{h["doc_id"]} - {h["section"]}]\n{h["content"]}\n");
        }
        return (rows, Stream(question, context.ToString(), ct));
    }

    async IAsyncEnumerable<string> Stream(string question, string context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, System),
            new(ChatRole.User, $"Context:\n{context}\n\nQuestion: {question}"),
        };
        await foreach (var u in a.MiniChat.GetStreamingResponseAsync(messages, new ChatOptions { Reasoning = new ReasoningOptions { Effort = ReasoningEffort.Low } }, ct))
            if (!string.IsNullOrEmpty(u.Text)) yield return u.Text;
    }

    /// <summary>One-time: embed title+description of every work order into work_orders.embedding.</summary>
    public async Task EmbedWorkOrdersAsync(Action<int>? progress = null, CancellationToken ct = default)
    {
        var pending = new List<(int Id, string Text)>();
        await using (var cmd = a.Db.CreateCommand("SELECT id, title || '. ' || description FROM work_orders WHERE embedding IS NULL ORDER BY id"))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) pending.Add((r.GetInt32(0), r.GetString(1)));

        int done = 0;
        foreach (var batch in pending.Chunk(500))
        {
            var embs = await a.Embeddings.GenerateAsync(batch.Select(b => b.Text), cancellationToken: ct);
            await using var conn = await a.Db.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            for (int i = 0; i < batch.Length; i++)
            {
                await using var up = new NpgsqlCommand("UPDATE work_orders SET embedding = @v::vector WHERE id = @id", conn, tx);
                up.Parameters.AddWithValue("v", "[" + string.Join(",", embs[i].Vector.ToArray().Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]");
                up.Parameters.AddWithValue("id", batch[i].Id);
                await up.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            progress?.Invoke(done += batch.Length);
        }
    }
}
