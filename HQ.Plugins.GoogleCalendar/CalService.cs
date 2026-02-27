using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleCalendar.Exceptions;
using HQ.Plugins.GoogleCalendar.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace HQ.Plugins.GoogleCalendar;

public class CalService
{
    private readonly CalendarService _calendarService;
    private static LogDelegate _logger = null;

    public CalService(ServiceConfig config, LogDelegate logDelegate)
    {
        _logger = logDelegate;
        var directory = $"{Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)}/GoogleCalendar";
        var files = Directory.GetFiles(directory, "*.TokenResponse-user");
        ICredential credential = null;

        if (files.Any())
        {
            if (files.Length == 1)
            {
                var fileName = files[0];
                credential = GoogleCredential.FromFile($"{directory}/{fileName}")
                    .CreateScoped(CalendarService.Scope.Calendar);   
            }
            else
            {
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
        }
        
        credential ??= GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = config.Credentials.ClientId,
                ClientSecret = config.Credentials.ClientSecret
            }, new[] { CalendarService.Scope.Calendar },
            config.Credentials.GoogleUser,
            CancellationToken.None,
            new FileDataStore(directory, true),
            new LocalServerCodeReceiver(config.LocalApiUrl)).Result;

        _calendarService = new CalendarService(new BaseClientService.Initializer()
        {
            HttpClientInitializer = credential,
            ApplicationName = "Ai Orchestrator - Google Calendar Plugin",
        });
    }

    public async Task<object> GetEvents(ServiceRequest serviceRequest)
    {
        var calendarId = !string.IsNullOrWhiteSpace(serviceRequest.CalendarId) 
            ? serviceRequest.CalendarId 
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
    
    public async Task<object> GetCalendar(ServiceRequest serviceRequest)
    {
        var calendarId = !string.IsNullOrWhiteSpace(serviceRequest.CalendarId) 
            ? serviceRequest.CalendarId 
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

    public async Task<object> GetCalendars(ServiceRequest serviceRequest)
    {
        try
        {
            var result = await _calendarService.CalendarList.List().ExecuteAsync();
            if (!string.IsNullOrWhiteSpace(serviceRequest.RequestingService))
            {
                return new OrchestratorRequest
                {
                    Service = serviceRequest.RequestingService,
                    ToolCallId = serviceRequest.ToolCallId
                };
            }

            return result;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error fetching event: {ex.Message}");
        }
    }
    
    public async Task<object> GetEvent(ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.EventId))
        {
            throw new Exception("Missing parameter: eventId.");
        }

        var eventId = serviceRequest.EventId;
        var calendarId = !string.IsNullOrWhiteSpace(serviceRequest.CalendarId) 
            ? serviceRequest.CalendarId 
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
    public async Task<object> EditEvent(ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.EventId) || string.IsNullOrWhiteSpace(serviceRequest.Summary))
        {
            throw new Exception("Missing required parameters: eventId, summary.");
        }

        var eventId = serviceRequest.EventId;
        var summary = serviceRequest.Summary;
        var calendarId = !string.IsNullOrWhiteSpace(serviceRequest.CalendarId) 
            ? serviceRequest.CalendarId
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
    
    public async Task<object> AddEvent(ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.Summary))
        {
            throw new Exception("Missing required parameters: summary.");
        }
        
        var calendarId = !string.IsNullOrWhiteSpace(serviceRequest.CalendarId) 
            ? serviceRequest.CalendarId
            : "primary";

        try
        {
            var calendarEvent = new Event
            {
                Attendees = serviceRequest.Attendees.Select(s => s.ToGoogleEventAttendee()).ToList(),
                Description = serviceRequest.Description,
                Location = serviceRequest.Location,
                Start = new EventDateTime
                {
                    DateTimeRaw = serviceRequest.StartDate.ToString()
                },
                End = new EventDateTime
                {
                    DateTimeRaw = serviceRequest.EndDate.ToString()
                },
                Reminders = new Event.RemindersData
                {
                    Overrides = serviceRequest.Reminders
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
    
    public async Task<object> GetEventsForDay(ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace("date"))
        {
            throw new Exception("Missing parameter: date.");
        }

        // Parse the input date
        if (!DateTime.TryParseExact(serviceRequest.Date.Value.ToString("yyyy-MM-dd"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new Exception("Invalid date format. Use 'yyyy-MM-dd'.");
        }

        if (!string.IsNullOrWhiteSpace(serviceRequest.CalendarId))
        {
            return await GetCalendar(serviceRequest.CalendarId, serviceRequest.Date.Value);
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

    public async Task<object> GetEventsForDateRange(ServiceRequest serviceRequest)
    {
        if (serviceRequest.StartDate == null || serviceRequest.EndDate == null)
        {
            throw new Exception("Missing parameters: startDate and endDate are required.");
        }

        // Validate date range
        if (serviceRequest.StartDate > serviceRequest.EndDate)
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
                    var events = await GetEventsInDateRange(calendar.Id, serviceRequest.StartDate.Value, serviceRequest.EndDate.Value);
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
    [Parameters("""{"type":"object","properties":{"calendarId":{"type":"string","description":"The calendar ID to retrieve events from. Defaults to 'primary'."}},"required":[]}""")]
    public async Task<object> GetCalendarEvents(ServiceConfig config, ServiceRequest request)
    {
        return await GetEvents(request);
    }

    [Display(Name = "get_calendar_event")]
    [Description("Retrieves a specific event from a Google Calendar by its event ID.")]
    [Parameters("""{"type":"object","properties":{"eventId":{"type":"string","description":"The ID of the event to retrieve"},"calendarId":{"type":"string","description":"The calendar ID. Defaults to 'primary'."}},"required":["eventId"]}""")]
    public async Task<object> GetCalendarEvent(ServiceConfig config, ServiceRequest request)
    {
        return await GetEvent(request);
    }

    [Display(Name = "edit_calendar_event")]
    [Description("Edits an existing event on a Google Calendar. Requires the event ID and new summary.")]
    [Parameters("""{"type":"object","properties":{"eventId":{"type":"string","description":"The ID of the event to edit"},"summary":{"type":"string","description":"The new summary/title for the event"},"calendarId":{"type":"string","description":"The calendar ID. Defaults to 'primary'."}},"required":["eventId","summary"]}""")]
    public async Task<object> EditCalendarEvent(ServiceConfig config, ServiceRequest request)
    {
        return await EditEvent(request);
    }

    [Display(Name = "get_calendar_events_for_day")]
    [Description("Retrieves all events across all calendars for a specific date. Optionally specify a calendarId to filter to one calendar.")]
    [Parameters("""{"type":"object","properties":{"date":{"type":"string","description":"The date to retrieve events for in yyyy-MM-dd format"},"calendarId":{"type":"string","description":"Optional calendar ID to filter events to a specific calendar"}},"required":["date"]}""")]
    public async Task<object> GetCalendarEventsForDay(ServiceConfig config, ServiceRequest request)
    {
        return await GetEventsForDay(request);
    }

    [Display(Name = "get_calendar_events_for_range")]
    [Description("Retrieves all events across all calendars for a date range.")]
    [Parameters("""{"type":"object","properties":{"startDate":{"type":"string","description":"The start date of the range in yyyy-MM-dd format"},"endDate":{"type":"string","description":"The end date of the range in yyyy-MM-dd format"}},"required":["startDate","endDate"]}""")]
    public async Task<object> GetCalendarEventsForRange(ServiceConfig config, ServiceRequest request)
    {
        return await GetEventsForDateRange(request);
    }

    [Display(Name = "get_calendars")]
    [Description("Retrieves a list of all available Google Calendars for the authenticated user.")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> GetAllCalendars(ServiceConfig config, ServiceRequest request)
    {
        return await GetCalendars(request);
    }

    [Display(Name = "get_calendar")]
    [Description("Retrieves details of a specific Google Calendar by its calendar ID.")]
    [Parameters("""{"type":"object","properties":{"calendarId":{"type":"string","description":"The calendar ID to retrieve. Defaults to 'primary'."}},"required":[]}""")]
    public async Task<object> GetSingleCalendar(ServiceConfig config, ServiceRequest request)
    {
        return await GetCalendar(request);
    }

    [Display(Name = "add_calendar_event")]
    [Description("Creates a new event on a Google Calendar with summary, start/end times, location, attendees, and reminders.")]
    [Parameters("""{"type":"object","properties":{"summary":{"type":"string","description":"The title/summary of the event"},"startDate":{"type":"string","description":"The start date and time of the event"},"endDate":{"type":"string","description":"The end date and time of the event"},"calendarId":{"type":"string","description":"The calendar ID. Defaults to 'primary'."},"location":{"type":"string","description":"The location of the event"},"description":{"type":"string","description":"A description of the event"},"attendees":{"type":"array","description":"List of attendees","items":{"type":"object","properties":{"email":{"type":"string"},"displayName":{"type":"string"}}}}},"required":["summary","startDate","endDate"]}""")]
    public async Task<object> AddCalendarEvent(ServiceConfig config, ServiceRequest request)
    {
        return await AddEvent(request);
    }
}