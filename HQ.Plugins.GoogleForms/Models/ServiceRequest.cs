using System.Text.Json;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleForms.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    public string FormId { get; set; }
    public string ResponseId { get; set; }

    // Create
    public string Title { get; set; }
    public string Description { get; set; }

    // add_questions — list of { title, type (TEXT|PARAGRAPH|RADIO|CHECKBOX|DROPDOWN), options[], required }
    public List<QuestionSpec> Questions { get; set; }
}

public record QuestionSpec
{
    public string Title { get; set; }
    public string Type { get; set; }            // TEXT | PARAGRAPH | RADIO | CHECKBOX | DROPDOWN
    public List<string> Options { get; set; }    // for choice types
    public bool? Required { get; set; }
}
