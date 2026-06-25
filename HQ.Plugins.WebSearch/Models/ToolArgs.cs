using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.WebSearch.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM.
/// </summary>

public class WebSearchArgs
{
    [Required, Description("The search query to look up on the web")]
    public string Query { get; set; }

    [Description("Maximum number of results to return. Defaults to 5.")]
    public int? MaxResults { get; set; }
}
