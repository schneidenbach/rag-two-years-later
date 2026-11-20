using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Lakeshore.Agents.WorkOrders;

/// <summary>
/// Structured analytics query the model fills in. Compiled to parameterized SQL over the work_order_facts view.
/// A structured analytics query: filters + dimensions + measures + optional time series.
/// </summary>
public record WorkOrderQuery
{
    [Description("Population filters. start_date and end_date are required.")]
    public required WorkOrderFilters Filters { get; init; }

    [Description("Break results down by these fields. Omit for a single overall row. Combine with timeSeries for trends per group (keep to one dimension when using timeSeries).")]
    public List<Dimension>? Dimensions { get; init; }

    [Description("Metrics to compute. Defaults to Count and AvgDaysToClose.")]
    public List<Measure>? Measures { get; init; }

    [Description("Set true to return results bucketed over time (by opened date).")]
    public bool? TimeSeries { get; init; }

    [Description("Time bucket when timeSeries is true. Defaults to Month.")]
    public TimeGrain? TimeGrain { get; init; }

    [Description("Optional ordering by one of the requested measures.")]
    public QueryOrder? OrderBy { get; init; }

    [Description("Maximum rows, 1-500. Defaults to 100.")]
    public int? Limit { get; init; }

    [Description("Drop groups with fewer than this many work orders (avoids noisy small groups).")]
    public int? MinimumGroupCount { get; init; }
}

public record WorkOrderFilters
{
    [Description("Inclusive start of the opened-date range, yyyy-MM-dd. Data covers 2025-06-01 through 2026-11-15.")]
    public required string StartDate { get; init; }

    [Description("Inclusive end of the opened-date range, yyyy-MM-dd.")]
    public required string EndDate { get; init; }

    [Description("Work order categories (trades): Plumbing, Electrical, HVAC, General Maintenance, Carpentry.")]
    public List<string>? Categories { get; init; }

    [Description("Subcategories, e.g. 'Medical Gas', 'Toilet / Flush Valve'. Use get_filter_values to discover.")]
    public List<string>? Subcategories { get; init; }

    [Description("Priorities: 'P1 Emergency', 'P2 Urgent', 'P3 Routine', 'P4 Scheduled'.")]
    public List<string>? Priorities { get; init; }

    [Description("Campuses: North Campus, South Campus, Riverside Campus.")]
    public List<string>? Campuses { get; init; }

    [Description("Building names. Use get_filter_values to discover.")]
    public List<string>? Buildings { get; init; }

    [Description("Location types, e.g. Patient Room, ICU, Operating Room, Public Restroom.")]
    public List<string>? LocationTypes { get; init; }

    [Description("Category of the currently assigned worker: Plumber, Electrician, HVAC Technician, General Maintenance, Carpenter.")]
    public List<string>? AssignedWorkerCategories { get; init; }

    [Description("Assigned worker names.")]
    public List<string>? AssignedWorkers { get; init; }

    [Description("Vendor parts were ordered from.")]
    public List<string>? PartsVendors { get; init; }

    [Description("Statuses: Open, In Progress, On Hold, Closed. Duration measures only use closed work orders.")]
    public List<string>? Statuses { get; init; }

    [Description("true = opened after hours (7pm-7am local), false = business hours.")]
    public bool? AfterHours { get; init; }

    [Description("true = only work orders that were reassigned at least once; false = never reassigned.")]
    public bool? WasReassigned { get; init; }

    [Description("true = only work orders that required ordering parts; false = no parts ordered.")]
    public bool? PartsOrdered { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<Dimension>))]
public enum Dimension
{
    Category, Subcategory, Priority, Campus, Building, Floor, LocationType, AssignedWorker, AssignedWorkerCategory,
    PartsVendor, AfterHours, Source, Status, RequestedBy, WasReassigned, PartsOrdered,
}

[JsonConverter(typeof(JsonStringEnumConverter<Measure>))]
public enum Measure
{
    [Description("Number of work orders.")] Count,
    [Description("Mean days from opened to closed (closed only).")] AvgDaysToClose,
    [Description("Median days from opened to closed.")] MedianDaysToClose,
    [Description("90th percentile days to close.")] P90DaysToClose,
    [Description("Mean days from opened until the assigned tech's first diagnosis.")] AvgDaysToFirstResponse,
    [Description("Mean days between parts ordered and parts received (only WOs that ordered parts).")] AvgDaysWaitingOnParts,
    [Description("Share of work orders that ordered parts (0-1).")] PartsOrderedRate,
    [Description("Share of work orders reassigned at least once (0-1).")] ReassignmentRate,
    [Description("Mean labor hours logged.")] AvgLaborHours,
    [Description("Number of work orders not yet closed.")] OpenCount,
}

[JsonConverter(typeof(JsonStringEnumConverter<TimeGrain>))]
public enum TimeGrain { Week, Month, Quarter }

public record QueryOrder
{
    public required Measure Measure { get; init; }
    [Description("asc or desc. Defaults to desc.")]
    public string? Direction { get; init; }
}
