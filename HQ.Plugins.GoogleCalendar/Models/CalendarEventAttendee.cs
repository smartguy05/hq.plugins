using Google.Apis.Calendar.v3.Data;

namespace HQ.Plugins.GoogleCalendar.Models;
public class CalendarEventAttendee
{
    public string Email { get; set; }
    public string Name { get; set; }
    public bool Optional { get; set; } = true;
    /// <summary>
    /// The attendee's response status. Possible values are: - "needsAction" - The attendee has not responded to the invitation (recommended for new events). - "declined" - The attendee has declined the invitation. - "tentative" - The attendee has tentatively accepted the invitation. - "accepted" - The attendee has accepted the invitation. Warning: If you add an event using the values declined, tentative, or accepted, attendees with the "Add invitations to my calendar" setting set to "When I respond to invitation in email" won't see an event on their calendar unless they choose to change their invitation response in the event invitation email.
    /// </summary>
    public string ResponseStatus { get; set; }

    public  EventAttendee ToGoogleEventAttendee()
    {
        return new EventAttendee
        {
            Email = Email,
            DisplayName = Name,
            Optional = Optional,
            ResponseStatus = ResponseStatus
        };
    }
}