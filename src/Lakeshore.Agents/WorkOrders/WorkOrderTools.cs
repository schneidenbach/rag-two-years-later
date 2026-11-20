using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Npgsql;

namespace Lakeshore.Agents.WorkOrders;

/// <summary>Read-only tools over the Lakeshore Health work-order database: macro (aggregate) -> micro (individual work orders).</summary>
public sealed class WorkOrderTools(NpgsqlDataSource db)
{
    public const string QueryToolName = "query_work_order_data";
    public const string FindToolName = "find_work_orders";
    public const string DetailsToolName = "get_work_order_details";
    public const string FilterValuesToolName = "get_filter_values";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public IEnumerable<AIFunction> AsAIFunctions() =>
    [
        AIFunctionFactory.Create(QueryAsync, QueryToolName),
        AIFunctionFactory.Create(FindAsync, FindToolName),
        AIFunctionFactory.Create(DetailsAsync, DetailsToolName),
        AIFunctionFactory.Create(FilterValuesAsync, FilterValuesToolName),
    ];

    [Description("""
        Aggregate analytics over hospital facilities work orders: counts, durations (days to close, days to first response,
        days waiting on parts), rates (parts ordered, reassignment), time series, rankings and grouped breakdowns.
        Use this FIRST to find where a problem is concentrated (macro view), then narrow filters step by step.
        Compare against a baseline (other categories, earlier periods, other locations) before concluding anything.
        Returns grouped rows, the matching population size and the SQL that ran.
        """)]
    public async Task<string> QueryAsync(
        [Description("The structured analytics query.")] WorkOrderQuery query,
        CancellationToken ct = default)
    {
        var built = WorkOrderSql.Aggregate(query, out var groupCols, out var measures);
        var rows = await ReadRowsAsync(built.Sql, built.Parameters, ct);

        var popParams = new List<NpgsqlParameter>();
        var popSql = $"SELECT count(*) FROM work_order_facts\n{WorkOrderSql.Where(query.Filters, popParams)}";
        await using var cmd = db.CreateCommand(popSql);
        cmd.Parameters.AddRange(popParams.ToArray());
        var population = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));

        var result = new JsonObject
        {
            ["kind"] = "aggregate",
            ["population"] = population,
            ["timeSeries"] = query.TimeSeries == true,
            ["groupBy"] = new JsonArray(groupCols.Select(c => (JsonNode)c).ToArray()),
            ["measures"] = new JsonArray(measures.Select(m => (JsonNode)WorkOrderSql.Snake(m.ToString())).ToArray()),
            ["rows"] = rows,
            ["sql"] = built.DisplaySql,
        };
        if (rows.Count == 0) result["note"] = "No work orders match these filters.";
        return result.ToJsonString(Json);
    }

    [Description("""
        List individual work orders matching filters (micro view) so you can see concrete examples behind an aggregate.
        Optionally search title/description text or action-log notes. Returns at most 25 rows with key timing fields.
        Use get_work_order_details next to read full action timelines.
        """)]
    public async Task<string> FindAsync(
        [Description("Population filters (same shape as query_work_order_data).")] WorkOrderFilters filters,
        [Description("Sort: LongestToClose (default), LongestWaitingOnParts, MostReassigned, Newest, Oldest.")] string? sortBy = null,
        [Description("Optional case-insensitive text to match in title or description.")] string? textContains = null,
        [Description("Optional case-insensitive text to match in any action-log note, e.g. 'backorder'.")] string? actionNotesContain = null,
        [Description("Max rows (1-25). Default 10.")] int limit = 10,
        CancellationToken ct = default)
    {
        var ps = new List<NpgsqlParameter>();
        var where = WorkOrderSql.Where(filters, ps);
        if (!string.IsNullOrWhiteSpace(textContains))
        {
            ps.Add(new($"p{ps.Count}", $"%{textContains}%"));
            where += $"\n  AND (title ILIKE @p{ps.Count - 1} OR f.id IN (SELECT id FROM work_orders WHERE description ILIKE @p{ps.Count - 1}))";
        }
        if (!string.IsNullOrWhiteSpace(actionNotesContain))
        {
            ps.Add(new($"p{ps.Count}", $"%{actionNotesContain}%"));
            where += $"\n  AND EXISTS (SELECT 1 FROM work_order_actions a WHERE a.work_order_id = f.id AND a.notes ILIKE @p{ps.Count - 1})";
        }
        var order = (sortBy ?? "LongestToClose").ToLowerInvariant() switch
        {
            "longestwaitingonparts" => "days_waiting_on_parts DESC NULLS LAST",
            "mostreassigned" => "reassignment_count DESC, days_to_close DESC NULLS LAST",
            "newest" => "opened_at DESC",
            "oldest" => "opened_at ASC",
            _ => "days_to_close DESC NULLS LAST",
        };
        var sql = $"""
            SELECT wo_number, title, priority, category, subcategory, campus, building, unit, location_type,
                   to_char(opened_at AT TIME ZONE '{WorkOrderSql.LocalTz}', 'YYYY-MM-DD HH24:MI') AS opened_local,
                   after_hours, status, round(days_to_close::numeric, 1) AS days_to_close,
                   round(days_waiting_on_parts::numeric, 1) AS days_waiting_on_parts,
                   assigned_worker, assigned_worker_category, reassignment_count, awaiting_parts_count, parts_vendor
            FROM work_order_facts f
            {where}
            ORDER BY {order}
            LIMIT {Math.Clamp(limit, 1, 25)}
            """;
        var rows = await ReadRowsAsync(sql, ps, ct);
        return new JsonObject
        {
            ["kind"] = "work_order_list",
            ["rows"] = rows,
            ["sql"] = WorkOrderSql.Inline(sql, ps),
        }.ToJsonString(Json);
    }

    [Description("Full detail for up to 5 work orders: description, location, and the complete action timeline (who did what, when, vendor, notes).")]
    public async Task<string> DetailsAsync(
        [Description("Work order numbers, e.g. ['WO-2026-008123'].")] string[] woNumbers,
        CancellationToken ct = default)
    {
        var numbers = woNumbers.Take(5).ToArray();
        var headerSql = $"""
            SELECT f.id, f.wo_number, f.title, wo.description, f.priority, f.category, f.subcategory, f.campus, f.building,
                   f.unit, f.location_type, f.requested_by, f.source, f.status, f.after_hours,
                   to_char(f.opened_at AT TIME ZONE '{WorkOrderSql.LocalTz}', 'YYYY-MM-DD HH24:MI') AS opened_local,
                   to_char(f.closed_at AT TIME ZONE '{WorkOrderSql.LocalTz}', 'YYYY-MM-DD HH24:MI') AS closed_local,
                   round(f.days_to_close::numeric, 1) AS days_to_close, f.assigned_worker
            FROM work_order_facts f JOIN work_orders wo ON wo.id = f.id
            WHERE f.wo_number = ANY(@nums)
            """;
        var headers = await ReadRowsAsync(headerSql, [new("nums", numbers)], ct);

        var actionsSql = $"""
            SELECT wo.wo_number, to_char(a.action_at AT TIME ZONE '{WorkOrderSql.LocalTz}', 'YYYY-MM-DD HH24:MI') AS at_local,
                   a.action_type, w.name AS worker, w.worker_category, v.name AS vendor, a.hours_spent, a.notes
            FROM work_order_actions a
            JOIN work_orders wo ON wo.id = a.work_order_id
            LEFT JOIN workers w ON w.id = a.worker_id
            LEFT JOIN vendors v ON v.id = a.vendor_id
            WHERE wo.wo_number = ANY(@nums)
            ORDER BY wo.wo_number, a.action_at
            """;
        var actions = await ReadRowsAsync(actionsSql, [new("nums", numbers)], ct);

        var list = new JsonArray();
        foreach (var h in headers.OfType<JsonObject>())
        {
            var num = h["wo_number"]!.GetValue<string>();
            h.Remove("id");
            var timeline = new JsonArray(actions.OfType<JsonObject>()
                .Where(a => a["wo_number"]!.GetValue<string>() == num)
                .Select(a => { var c = (JsonObject)a.DeepClone(); c.Remove("wo_number"); return (JsonNode)c; }).ToArray());
            h["actions"] = timeline;
            list.Add(h.DeepClone());
        }
        return new JsonObject { ["kind"] = "work_order_details", ["workOrders"] = list }.ToJsonString(Json);
    }

    [Description("Distinct values (with counts) for a field, so filters use exact names. Fields: Category, Subcategory, Priority, Campus, Building, LocationType, AssignedWorker, AssignedWorkerCategory, PartsVendor, Source, Status, RequestedBy.")]
    public async Task<string> FilterValuesAsync(
        [Description("The field to list values for.")] Dimension field,
        CancellationToken ct = default)
    {
        var col = WorkOrderSql.DimensionColumn(field);
        var rows = await ReadRowsAsync($"SELECT {col} AS value, count(*) AS work_orders FROM work_order_facts GROUP BY 1 ORDER BY 2 DESC LIMIT 200", [], ct);
        return new JsonObject { ["kind"] = "filter_values", ["field"] = field.ToString(), ["values"] = rows }.ToJsonString(Json);
    }

    async Task<JsonArray> ReadRowsAsync(string sql, IEnumerable<NpgsqlParameter> ps, CancellationToken ct)
    {
        await using var conn = await db.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY; SET LOCAL statement_timeout = '15s';", conn, tx)) await ro.ExecuteNonQueryAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        foreach (var p in ps) cmd.Parameters.Add(p.Clone());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new JsonArray();
        while (await reader.ReadAsync(ct))
        {
            var row = new JsonObject();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var v = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = v switch
                {
                    null => null,
                    DateOnly d => d.ToString("yyyy-MM-dd"),
                    DateTime dt => dt.ToString("yyyy-MM-dd"),
                    decimal m => JsonValue.Create(m),
                    double dbl => JsonValue.Create(Math.Round(dbl, 3)),
                    long l => JsonValue.Create(l),
                    int n => JsonValue.Create(n),
                    bool b => JsonValue.Create(b),
                    _ => JsonValue.Create(v.ToString()),
                };
            }
            rows.Add(row);
        }
        return rows;
    }
}
