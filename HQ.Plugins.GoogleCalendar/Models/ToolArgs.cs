using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using Google.Apis.Calendar.v3.Data;

namespace HQ.Plugins.GoogleCalendar.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

/// <summary>
/// Args for get_calendars — no LLM-facing parameters, but the tool relays through the
/// orchestrator when invoked by another service, so it needs the framework routing fields.
/// </summary>
public class GetCalendarsArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }
}

public class GetCalendarEventsArgs
{
    [Description("The calendar ID to retrieve events from. Defaults to 'primary'.")]
    public string CalendarId { get; set; }
}

public class GetCalendarEventArgs
{
    [Required, Description("The ID of the event to retrieve")]
    public string EventId { get; set; }

    [Description("The calendar ID. Defaults to 'primary'.")]
    public string CalendarId { get; set; }
}

public class EditCalendarEventArgs
{
    [Required, Description("The ID of the event to edit")]
    public string EventId { get; set; }

    [Required, Description("The new summary/title for the event")]
    public string Summary { get; set; }

    [Description("The calendar ID. Defaults to 'primary'.")]
    public string CalendarId { get; set; }
}

public class GetCalendarEventsForDayArgs
{
    [Required, Description("The date to retrieve events for in yyyy-MM-dd format")]
    public DateTime? Date { get; set; }

    [Description("Optional calendar ID to filter events to a specific calendar")]
    public string CalendarId { get; set; }
}

public class GetCalendarEventsForRangeArgs
{
    [Required, Description("The start date of the range in yyyy-MM-dd format")]
    public DateTime? StartDate { get; set; }

    [Required, Description("The end date of the range in yyyy-MM-dd format")]
    public DateTime? EndDate { get; set; }
}

public class GetSingleCalendarArgs
{
    [Description("The calendar ID to retrieve. Defaults to 'primary'.")]
    public string CalendarId { get; set; }
}

public class AddCalendarEventArgs
{
    [Required, Description("The title/summary of the event")]
    public string Summary { get; set; }

    [Required, Description("The start date and time of the event")]
    public DateTime? StartDate { get; set; }

    [Required, Description("The end date and time of the event")]
    public DateTime? EndDate { get; set; }

    [Description("The calendar ID. Defaults to 'primary'.")]
    public string CalendarId { get; set; }

    [Description("The location of the event")]
    public string Location { get; set; }

    [Description("A description of the event")]
    public string Description { get; set; }

    [Description("List of attendees")]
    public List<CalendarEventAttendee> Attendees { get; set; }

    /// <summary>Reminder overrides applied to the created event; not advertised to the model.</summary>
    [Injected]
    public List<EventReminder> Reminders { get; set; }
}
