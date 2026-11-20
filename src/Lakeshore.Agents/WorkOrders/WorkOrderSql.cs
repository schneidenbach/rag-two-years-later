using System.Globalization;
using System.Text;
using Npgsql;

namespace Lakeshore.Agents.WorkOrders;

/// <summary>Compiles <see cref="WorkOrderQuery"/> into parameterized, read-only SQL. Only whitelisted columns are ever emitted.</summary>
public static class WorkOrderSql
{
    public const string LocalTz = "America/Chicago";

    public static string DimensionColumn(Dimension d) => d switch
    {
        Dimension.Category => "category",
        Dimension.Subcategory => "subcategory",
        Dimension.Priority => "priority",
        Dimension.Campus => "campus",
        Dimension.Building => "building",
        Dimension.Floor => "floor",
        Dimension.LocationType => "location_type",
        Dimension.AssignedWorker => "assigned_worker",
        Dimension.AssignedWorkerCategory => "assigned_worker_category",
        Dimension.PartsVendor => "coalesce(parts_vendor, '(no parts ordered)')",
        Dimension.AfterHours => "after_hours",
        Dimension.Source => "source",
        Dimension.Status => "status",
        Dimension.RequestedBy => "requested_by",
        Dimension.WasReassigned => "(reassignment_count > 0)",
        Dimension.PartsOrdered => "(parts_vendor IS NOT NULL)",
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public static string MeasureSql(Measure m) => m switch
    {
        Measure.Count => "count(*)",
        Measure.AvgDaysToClose => "round(avg(days_to_close)::numeric, 2)",
        Measure.MedianDaysToClose => "round((percentile_cont(0.5) WITHIN GROUP (ORDER BY days_to_close))::numeric, 2)",
        Measure.P90DaysToClose => "round((percentile_cont(0.9) WITHIN GROUP (ORDER BY days_to_close))::numeric, 2)",
        Measure.AvgDaysToFirstResponse => "round(avg(days_to_first_response)::numeric, 2)",
        Measure.AvgDaysWaitingOnParts => "round(avg(days_waiting_on_parts)::numeric, 2)",
        Measure.PartsOrderedRate => "round(avg(CASE WHEN parts_vendor IS NOT NULL THEN 1.0 ELSE 0 END), 3)",
        Measure.ReassignmentRate => "round(avg(CASE WHEN reassignment_count > 0 THEN 1.0 ELSE 0 END), 3)",
        Measure.AvgLaborHours => "round(avg(labor_hours)::numeric, 2)",
        Measure.OpenCount => "count(*) FILTER (WHERE status <> 'Closed')",
        _ => throw new ArgumentOutOfRangeException(nameof(m)),
    };

    public static string Snake(string s) => string.Concat(s.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    public sealed class Built
    {
        public string Sql { get; set; } = "";
        public List<NpgsqlParameter> Parameters { get; } = [];
        public string DisplaySql { get; set; } = "";
    }

    /// <summary>Builds the WHERE clause shared by all tools.</summary>
    public static string Where(WorkOrderFilters f, List<NpgsqlParameter> ps)
    {
        var sb = new StringBuilder("WHERE 1=1");
        string P(object v) { var name = $"p{ps.Count}"; ps.Add(new NpgsqlParameter(name, v)); return "@" + name; }

        var start = ParseDate(f.StartDate, nameof(f.StartDate));
        var end = ParseDate(f.EndDate, nameof(f.EndDate)).AddDays(1);
        sb.Append($"\n  AND opened_at >= ({P(start)}::timestamp AT TIME ZONE '{LocalTz}')");
        sb.Append($"\n  AND opened_at <  ({P(end)}::timestamp AT TIME ZONE '{LocalTz}')");

        void In(string col, List<string>? vals) { if (vals is { Count: > 0 }) sb.Append($"\n  AND {col} = ANY({P(vals.ToArray())})"); }
        In("category", f.Categories);
        In("subcategory", f.Subcategories);
        In("priority", f.Priorities);
        In("campus", f.Campuses);
        In("building", f.Buildings);
        In("location_type", f.LocationTypes);
        In("assigned_worker_category", f.AssignedWorkerCategories);
        In("assigned_worker", f.AssignedWorkers);
        In("parts_vendor", f.PartsVendors);
        In("status", f.Statuses);
        if (f.AfterHours is bool ah) sb.Append($"\n  AND after_hours = {P(ah)}");
        if (f.WasReassigned is bool r) sb.Append(r ? "\n  AND reassignment_count > 0" : "\n  AND reassignment_count = 0");
        if (f.PartsOrdered is bool po) sb.Append(po ? "\n  AND parts_vendor IS NOT NULL" : "\n  AND parts_vendor IS NULL");
        return sb.ToString();
    }

    public static Built Aggregate(WorkOrderQuery q, out List<string> groupColumns, out List<Measure> measures)
    {
        var b = new Built();
        measures = q.Measures is { Count: > 0 } ? q.Measures.Distinct().ToList() : [Measure.Count, Measure.AvgDaysToClose];
        var dims = (q.Dimensions ?? []).Distinct().ToList();
        groupColumns = [];
        var select = new List<string>();
        var group = new List<string>();

        if (q.TimeSeries == true)
        {
            var grain = (q.TimeGrain ?? TimeGrain.Month).ToString().ToLowerInvariant();
            var expr = $"date_trunc('{grain}', opened_at AT TIME ZONE '{LocalTz}')::date";
            select.Add($"{expr} AS period"); group.Add(expr); groupColumns.Add("period");
        }
        foreach (var d in dims)
        {
            var alias = Snake(d.ToString());
            select.Add($"{DimensionColumn(d)} AS {alias}"); group.Add(DimensionColumn(d)); groupColumns.Add(alias);
        }
        foreach (var m in measures) select.Add($"{MeasureSql(m)} AS {Snake(m.ToString())}");

        var where = Where(q.Filters, b.Parameters);
        // Duration columns are NULL for open work orders, so averages/percentiles naturally cover closed work only.

        var sb = new StringBuilder($"SELECT {string.Join(",\n       ", select)}\nFROM work_order_facts\n{where}");
        if (group.Count > 0) sb.Append($"\nGROUP BY {string.Join(", ", group)}");
        if (q.MinimumGroupCount is int min && group.Count > 0) sb.Append($"\nHAVING count(*) >= {Math.Clamp(min, 1, 100000)}");

        var order = new List<string>();
        if (q.OrderBy is { } ob)
            order.Add($"{Snake(ob.Measure.ToString())} {(string.Equals(ob.Direction, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC")} NULLS LAST");
        if (q.TimeSeries == true) order.Insert(0, "period");
        if (order.Count == 0 && group.Count > 0) order.Add("1");
        if (order.Count > 0) sb.Append($"\nORDER BY {string.Join(", ", order)}");
        sb.Append($"\nLIMIT {Math.Clamp(q.Limit ?? 100, 1, 500)}");

        b.Sql = sb.ToString();
        b.DisplaySql = Inline(b.Sql, b.Parameters);
        return b;
    }

    /// <summary>Inlines parameter values for on-screen display only. Never executed.</summary>
    public static string Inline(string sql, IEnumerable<NpgsqlParameter> ps)
    {
        foreach (var p in ps.OrderByDescending(p => p.ParameterName.Length))
        {
            string lit = p.Value switch
            {
                string[] arr => "ARRAY[" + string.Join(", ", arr.Select(a => $"'{a.Replace("'", "''")}'")) + "]",
                DateTime dt => $"'{dt:yyyy-MM-dd}'",
                bool bl => bl ? "true" : "false",
                string s => $"'{s.Replace("'", "''")}'",
                var o => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "NULL",
            };
            sql = sql.Replace("@" + p.ParameterName, lit);
        }
        return sql;
    }

    static DateTime ParseDate(string s, string name) =>
        DateTime.TryParseExact(s?.Trim()[..Math.Min(10, s.Trim().Length)], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : throw new ArgumentException($"{name} must be yyyy-MM-dd (got '{s}').");
}
