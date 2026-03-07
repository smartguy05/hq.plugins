using HQ.Models.Attributes;

namespace HQ.Plugins.GoogleCalendar.Models;

public record GoogleApiCredentials
{
    [Tooltip("OAuth 2.0 Client ID from Google Cloud Console")]
    public string ClientId { get; init; }

    [Tooltip("OAuth 2.0 Client Secret from Google Cloud Console")]
    public string ClientSecret { get; init; }

    [Tooltip("Google account email to access calendars for, e.g. user@gmail.com")]
    public string GoogleUser { get; init; }
}
