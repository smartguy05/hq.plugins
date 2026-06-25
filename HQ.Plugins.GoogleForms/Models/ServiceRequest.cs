using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleForms.Models;

/// <summary>
/// Framework request envelope (the <c>T</c> in <c>CommandBase&lt;T, ServiceConfig&gt;</c>). Carries
/// only the orchestrator-supplied routing fields; per-tool LLM arguments now live on each tool's
/// dedicated args type (see <c>ToolArgs.cs</c>) and are bound by <c>ProcessRequest</c>.
/// </summary>
public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
}

/// <summary>Nested question payload for add_questions. One per question to append.</summary>
public record QuestionSpec
{
    [Required]
    public string Title { get; set; }

    [Required]
    [Description("TEXT | PARAGRAPH | RADIO | CHECKBOX | DROPDOWN")]
    public string Type { get; set; }            // TEXT | PARAGRAPH | RADIO | CHECKBOX | DROPDOWN

    public List<string> Options { get; set; }    // for choice types
    public bool? Required { get; set; }
}
