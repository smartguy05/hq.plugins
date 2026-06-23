using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.ReportGenerator.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class GenerateReportArgs
{
    [Required, Description("Report title")]
    public string Title { get; set; }

    [Required, Description("Report content in Markdown format")]
    public string Content { get; set; }

    [Description("Output format: html or markdown (default: html)")]
    public string Format { get; set; }

    [Description("Output filename without extension (auto-generated from title and date if empty)")]
    public string FileName { get; set; }
}

public class GetReportArgs
{
    [Required, Description("The report ID returned from generate_report or list_reports")]
    public string ReportId { get; set; }
}
