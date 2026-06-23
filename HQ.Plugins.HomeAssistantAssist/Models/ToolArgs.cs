using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.HomeAssistantVoice.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM.
/// </summary>
public class HomeAssistantCommandArgs
{
    [Required, Description("The natural language command to send to Home Assistant")]
    public string Query { get; set; }
}
