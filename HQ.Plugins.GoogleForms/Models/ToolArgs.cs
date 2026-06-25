using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;

namespace HQ.Plugins.GoogleForms.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. There is no hand-written parameter JSON; each tool method references its args type via
/// <c>[Parameters(typeof(...))]</c>.
/// </summary>

public class CreateFormArgs
{
    [Required]
    public string Title { get; set; }

    public string Description { get; set; }
}

public class GetFormArgs
{
    [Required]
    public string FormId { get; set; }
}

public class AddQuestionsArgs
{
    [Required]
    public string FormId { get; set; }

    [Required]
    public List<QuestionSpec> Questions { get; set; }
}

public class ListResponsesArgs
{
    [Required]
    public string FormId { get; set; }
}

public class GetResponseArgs
{
    [Required]
    public string FormId { get; set; }

    [Required]
    public string ResponseId { get; set; }
}
