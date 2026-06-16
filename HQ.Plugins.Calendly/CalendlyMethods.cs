namespace HQ.Plugins.Calendly;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on CalendlyService.</summary>
public static class CalendlyMethods
{
    public const string GetCurrentUser = "get_current_user";
    public const string ListEventTypes = "list_event_types";
    public const string CreateSchedulingLink = "create_scheduling_link";
    public const string ListScheduledEvents = "list_scheduled_events";
    public const string GetScheduledEvent = "get_scheduled_event";
    public const string ListEventInvitees = "list_event_invitees";
    public const string CancelEvent = "cancel_event";
}
