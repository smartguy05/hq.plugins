using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.Calendly.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class ListEventTypesArgs
{
    [Description("User URI (defaults to the authenticated user)")]
    public string UserUri { get; set; }

    [Description("Max results (default 25)")]
    public int? Count { get; set; }
}

public class CreateSchedulingLinkArgs
{
    [Required, Description("The event type URI to book against")]
    public string EventTypeUri { get; set; }
}

public class ListScheduledEventsArgs
{
    [Description("User URI (defaults to the authenticated user)")]
    public string UserUri { get; set; }

    [Description("active | canceled")]
    public string Status { get; set; }

    [Description("Max results (default 25)")]
    public int? Count { get; set; }
}

public class GetScheduledEventArgs
{
    [Required, Description("Scheduled event URI or UUID")]
    public string EventUri { get; set; }
}

public class ListEventInviteesArgs
{
    [Required, Description("Scheduled event URI or UUID")]
    public string EventUri { get; set; }
}

public class CancelCalendlyEventArgs
{
    [Required, Description("Scheduled event URI or UUID")]
    public string EventUri { get; set; }

    [Description("Cancellation reason shown to the invitee")]
    public string Reason { get; set; }
}
