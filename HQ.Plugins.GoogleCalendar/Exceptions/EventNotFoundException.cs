namespace HQ.Plugins.GoogleCalendar.Exceptions;

public class EventNotFoundException : Exception
{
    public EventNotFoundException()
    {
    }

    public EventNotFoundException(string message) : base(message)
    {
    }

    public EventNotFoundException(string message, Exception inner) : base(message, inner)
    {
    }
}