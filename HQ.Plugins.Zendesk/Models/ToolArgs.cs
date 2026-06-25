using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using HQ.Models.Helpers;

namespace HQ.Plugins.Zendesk.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable). ID fields preserve <see cref="StringOrNumberConverter"/>
/// so values arriving as either strings or numbers bind correctly.
/// </summary>

public class SearchTicketsArgs
{
    [Required, Description("Zendesk search query (the 'type:ticket' filter is added automatically)")]
    public string Query { get; set; }

    [Description("Max results (default 25)")]
    public int? PageSize { get; set; }
}

public class GetTicketArgs
{
    [Required, Description("The ticket ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string TicketId { get; set; }
}

public class CreateTicketArgs
{
    [Required]
    public string Subject { get; set; }

    [Required, Description("Initial comment / description body")]
    public string Comment { get; set; }

    [Description("Requester user ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string RequesterId { get; set; }

    [Description("Assignee agent ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string AssigneeId { get; set; }

    [Description("low | normal | high | urgent")]
    public string Priority { get; set; }

    [Description("new | open | pending | hold | solved | closed")]
    public string Status { get; set; }

    [Description("Comma-separated tags")]
    public string Tags { get; set; }

    /// <summary>Read by the tool body (defaults to public) but not advertised to the model.</summary>
    [Injected]
    public bool? Public { get; set; }
}

public class UpdateTicketArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string TicketId { get; set; }

    [Description("new | open | pending | hold | solved | closed")]
    public string Status { get; set; }

    [Description("low | normal | high | urgent")]
    public string Priority { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    public string AssigneeId { get; set; }

    [Description("Comma-separated tags (replaces existing)")]
    public string Tags { get; set; }
}

public class AddTicketCommentArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string TicketId { get; set; }

    [Required, Description("Comment body")]
    public string Comment { get; set; }

    [Description("true = customer-facing reply, false = internal note. Default true.")]
    public bool? Public { get; set; }
}

public class ListTicketsArgs
{
    [Description("Max results (default 25)")]
    public int? PageSize { get; set; }
}

public class GetUserArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string UserId { get; set; }
}

public class SearchUsersArgs
{
    [Required, Description("Search text (name or email)")]
    public string Query { get; set; }
}

public class ListMacrosArgs
{
    [Description("Max results (default 50)")]
    public int? PageSize { get; set; }
}

public class ApplyMacroArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string TicketId { get; set; }

    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string MacroId { get; set; }
}
