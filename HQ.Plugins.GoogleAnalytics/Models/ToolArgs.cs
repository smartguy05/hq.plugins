using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.GoogleAnalytics.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Report shaping uses comma-separated strings to keep the schema simple.
/// </summary>

public class RunReportArgs
{
    [Description("GA4 property ID (numeric)")]
    public string PropertyId { get; set; }

    [Description("Comma-separated dimension names")]
    public string Dimensions { get; set; }

    [Required, Description("Comma-separated metric names")]
    public string Metrics { get; set; }

    [Description("YYYY-MM-DD, or '7daysAgo'/'today'")]
    public string StartDate { get; set; }

    [Description("YYYY-MM-DD, or 'today'")]
    public string EndDate { get; set; }

    [Description("Max rows (default 100)")]
    public int? Limit { get; set; }
}

public class RunRealtimeReportArgs
{
    public string PropertyId { get; set; }

    [Description("Comma-separated dimension names, e.g. 'country'")]
    public string Dimensions { get; set; }

    [Required, Description("Comma-separated metric names, e.g. 'activeUsers'")]
    public string Metrics { get; set; }

    [Description("Max rows (default 100)")]
    public int? Limit { get; set; }
}

public class GetMetadataArgs
{
    public string PropertyId { get; set; }
}
