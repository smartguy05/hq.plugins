using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleCalendar.Exceptions;
using HQ.Plugins.GoogleCalendar.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;

namespace HQ.Plugins.GoogleCalendar;

public class CalService
{
    private readonly CalendarService _calendarService;
    private static LogDelegate _logger = null;

    public CalService(ServiceConfig config, LogDelegate logDelegate)
    {
        _logger = logDelegate;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = config.Credentials.ClientId,
                ClientSecret = config.Credentials.ClientSecret
            },
            Scopes = [CalendarService.Scope.Calendar]
        });

        var credential = new UserCredential(flow, config.Credentials.GoogleUser, new TokenResponse
        {
            RefreshToken = config.Credentials.RefreshToken
        });

        _calendarService = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Ai Orchestrator - Google Calendar Plugin",
        });
    }

    public async Task<object> GetEvents(string calendarId)
    {
        calendarId = !string.IsNullOrWhiteSpace(calendarId)
            ? calendarId
            : "primary";
        try
        {
            return await _calendarService.Events.List(calendarId).ExecuteAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching event: {ex.Message}");
        }
    }

    public async Task<object> GetCalendar(string calendarId)
    {
        calendarId = !string.IsNullOrWhiteSpace(calendarId)
            ? calendarId
            : "primary";
        try
        {
            return await _calendarService.Calendars.Get(calendarId).ExecuteAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching event: {ex.Message}");
        }
    }

    public async Task<object> GetCalendars(GetCalendarsArgs request)
    {
        try
        {
            var result = await _calendarService.CalendarList.List().ExecuteAsync();
            if (!string.IsNullOrWhiteSpace(request.RequestingService))
            {
                return new OrchestratorRequest
                {
                    Service = request.RequestingService,
                    ToolCallId = request.ToolCallId
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching event: {ex.Message}");
        }
    }

    public async Task<object> GetEvent(string eventId, string calendarId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new Exception("Missing parameter: eventId.");
        }

        calendarId = !string.IsNullOrWhiteSpace(calendarId)
            ? calendarId
            : "primary";

        try
        {
            return await _calendarService.Events.Get(calendarId, eventId).ExecuteAsync();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching event: {ex.Message}");
        }
    }

    // Edit event details on Google Calendar
    public async Task<object> EditEvent(EditCalendarEventArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.EventId) || string.IsNullOrWhiteSpace(request.Summary))
        {
            throw new Exception("Missing required parameters: eventId, summary.");
        }

        var eventId = request.EventId;
        var summary = request.Summary;
        var calendarId = !string.IsNullOrWhiteSpace(request.CalendarId)
            ? request.CalendarId
            : "primary";

        try
        {
            var eventDetail = await _calendarService.Events.Get(calendarId, eventId).ExecuteAsync();
            eventDetail.Summary = summary;

            // Optionally edit other fields (start, end, etc.)

            var updatedEvent = await _calendarService.Events.Update(eventDetail, calendarId, eventId).ExecuteAsync();

            return new
            {
                EventId = updatedEvent.Id,
                updatedEvent.Summary,
                Start = updatedEvent.Start.DateTimeRaw,
                End = updatedEvent.End.DateTimeRaw
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"Error updating event: {ex.Message}");
        }
    }

    public async Task<object> AddEvent(AddCalendarEventArgs request)
    {
        if (string.IsNullOrWhiteSpace(request.Summary))
        {
            throw new Exception("Missing required parameters: summary.");
        }

        var calendarId = !string.IsNullOrWhiteSpace(request.CalendarId)
            ? request.CalendarId
            : "primary";

        try
        {
            var calendarEvent = new Event
            {
                Attendees = request.Attendees.Select(s => s.ToGoogleEventAttendee()).ToList(),
                Description = request.Description,
                Location = request.Location,
                Start = new EventDateTime
                {
                    DateTimeRaw = request.StartDate.ToString()
                },
                End = new EventDateTime
                {
                    DateTimeRaw = request.EndDate.ToString()
                },
                Reminders = new Event.RemindersData
                {
                    Overrides = request.Reminders
                }
            };
            var insertRequest = await _calendarService.Events.Insert(calendarEvent, calendarId).ExecuteAsync();

            return new
            {
                EventId = insertRequest.Id,
                insertRequest.Summary,
                Start = insertRequest.Start.DateTimeRaw,
                End = insertRequest.End.DateTimeRaw,
                Attendees = insertRequest.Attendees
            };
        }
        catch (Exception ex)
        {
            throw new Exception($"Error updating event: {ex.Message}");
        }
    }

    public async Task<object> GetEventsForDay(GetCalendarEventsForDayArgs request)
    {
        if (string.IsNullOrWhiteSpace("date"))
        {
            throw new Exception("Missing parameter: date.");
        }

        // Parse the input date
        if (!DateTime.TryParseExact(request.Date.Value.ToString("yyyy-MM-dd"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new Exception("Invalid date format. Use 'yyyy-MM-dd'.");
        }

        if (!string.IsNullOrWhiteSpace(request.CalendarId))
        {
            return await GetCalendar(request.CalendarId, request.Date.Value);
        }

        var calendarList = await _calendarService.CalendarList.List().ExecuteAsync();
        List<object> calendarItems = new();
        if (calendarList.Items.Any())
        {
            for (var i = 0; i < calendarList.Items.Count; i++)
            {
                try
                {
                    var items = (await GetCalendar(calendarList.Items[i].Id, date.Date)).ToList();
                    if (items.Any())
                    {
                        calendarItems.AddRange(items);
                    }
                }
                catch (EventNotFoundException e)
                {
                    // no-op
                }
                catch (Exception ex)
                {
                    await _logger(LogLevel.Error, ex.Message, ex);
                }
            }
            return calendarItems;
        }

        return null;
    }

    public async Task<object> GetEventsForDateRange(GetCalendarEventsForRangeArgs request)
    {
        if (request.StartDate == null || request.EndDate == null)
        {
            throw new Exception("Missing parameters: startDate and endDate are required.");
        }

        // Validate date range
        if (request.StartDate > request.EndDate)
        {
            throw new Exception("Start date must be before or equal to end date.");
        }

        var calendarList = await _calendarService.CalendarList.List().ExecuteAsync();
        List<object> allEvents = new();

        if (calendarList.Items.Any())
        {
            foreach (var calendar in calendarList.Items)
            {
                try
                {
                    var events = await GetEventsInDateRange(calendar.Id, request.StartDate.Value, request.EndDate.Value);
                    allEvents.AddRange(events);
                }
                catch (Exception ex)
                {
                    await _logger(LogLevel.Error, $"Error fetching events for calendar {calendar.Id}: {ex.Message}", ex);
                }
            }
        }

        return allEvents;
    }

    private async Task<IEnumerable<object>> GetEventsInDateRange(string calendarId, DateTime startDate, DateTime endDate)
    {
        try
        {
            var request = _calendarService.Events.List(calendarId);
            request.TimeMin = startDate;
            request.TimeMax = endDate.AddDays(1).AddTicks(-1); // Include full end date
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var events = await request.ExecuteAsync();

            return events.Items.Select(e => new
            {
                CalendarId = calendarId,
                EventId = e.Id,
                Summary = e.Summary,
                Start = e.Start.DateTimeRaw ?? e.Start.Date,
                End = e.End.DateTimeRaw ?? e.End.Date
            }).ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching events in date range: {ex.Message}");
        }
    }

    private async Task<IEnumerable<object>> GetCalendar(string calendarId, DateTime date)
    {
        try
        {
            // Set the timeMin and timeMax to filter events for the specific day
            var request = _calendarService.Events.List(calendarId);
            request.TimeMin = date;
            request.TimeMax = date.AddDays(1).AddTicks(-1); // End of the day
            request.ShowDeleted = false;
            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

            var events = await request.ExecuteAsync();

            // Format the event list
            return events.Items.Select(e => new
            {
                EventId = e.Id,
                Summary = e.Summary,
                Start = e.Start.DateTimeRaw ?? e.Start.Date, // In case it's an all-day event
                End = e.End.DateTimeRaw ?? e.End.Date
            }).ToList();
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching events: {ex.Message}");
        }
    }

    // --- Annotated wrapper methods for tool definition scanning ---

    [Display(Name = "get_calendar_events")]
    [Description("Retrieves all events from a Google Calendar. Optionally specify a calendarId, defaults to primary calendar.")]
    [Parameters(typeof(GetCalendarEventsArgs))]
    public async Task<object> GetCalendarEvents(ServiceConfig config, GetCalendarEventsArgs request)
    {
        return await GetEvents(request.CalendarId);
    }

    [Display(Name = "get_calendar_event")]
    [Description("Retrieves a specific event from a Google Calendar by its event ID.")]
    [Parameters(typeof(GetCalendarEventArgs))]
    public async Task<object> GetCalendarEvent(ServiceConfig config, GetCalendarEventArgs request)
    {
        return await GetEvent(request.EventId, request.CalendarId);
    }

    [Display(Name = "edit_calendar_event")]
    [Description("Edits an existing event on a Google Calendar. Requires the event ID and new summary.")]
    [Parameters(typeof(EditCalendarEventArgs))]
    public async Task<object> EditCalendarEvent(ServiceConfig config, EditCalendarEventArgs request)
    {
        return await EditEvent(request);
    }

    [Display(Name = "get_calendar_events_for_day")]
    [Description("Retrieves all events across all calendars for a specific date. Optionally specify a calendarId to filter to one calendar.")]
    [Parameters(typeof(GetCalendarEventsForDayArgs))]
    public async Task<object> GetCalendarEventsForDay(ServiceConfig config, GetCalendarEventsForDayArgs request)
    {
        return await GetEventsForDay(request);
    }

    [Display(Name = "get_calendar_events_for_range")]
    [Description("Retrieves all events across all calendars for a date range.")]
    [Parameters(typeof(GetCalendarEventsForRangeArgs))]
    public async Task<object> GetCalendarEventsForRange(ServiceConfig config, GetCalendarEventsForRangeArgs request)
    {
        return await GetEventsForDateRange(request);
    }

    [Display(Name = "get_calendars")]
    [Description("Retrieves a list of all available Google Calendars for the authenticated user.")]
    [Parameters(typeof(GetCalendarsArgs))]
    public async Task<object> GetAllCalendars(ServiceConfig config, GetCalendarsArgs request)
    {
        return await GetCalendars(request);
    }

    [Display(Name = "get_calendar")]
    [Description("Retrieves details of a specific Google Calendar by its calendar ID.")]
    [Parameters(typeof(GetSingleCalendarArgs))]
    public async Task<object> GetSingleCalendar(ServiceConfig config, GetSingleCalendarArgs request)
    {
        return await GetCalendar(request.CalendarId);
    }

    [Display(Name = "add_calendar_event")]
    [Description("Creates a new event on a Google Calendar with summary, start/end times, location, attendees, and reminders.")]
    [Parameters(typeof(AddCalendarEventArgs))]
    public async Task<object> AddCalendarEvent(ServiceConfig config, AddCalendarEventArgs request)
    {
        return await AddEvent(request);
    }
}
