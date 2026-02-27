using HQ.Models.Interfaces;
using Google.Apis.Calendar.v3.Data;

namespace HQ.Plugins.GoogleCalendar.Models;

public record ServiceRequest: IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    public string EventId { get; init; }
    public string CalendarId { get; init; }
    public string Summary { get; init; }
    public string Location { get; set; }
    public string Description { get; set; }
    public DateTime? Date { get; init; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<CalendarEventAttendee> Attendees { get; set; }
    public List<EventReminder> Reminders { get; set; }
}