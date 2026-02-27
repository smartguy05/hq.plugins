namespace HQ.Plugins.GoogleCalendar.Models;

public record GoogleApiCredentials
{
    public string ClientId { get; init; }
    public string ClientSecret { get; init; }
    public string GoogleUser { get; init; }
}