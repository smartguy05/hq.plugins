using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleCalendar.Models;

public record ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google OAuth 2.0 credentials for calendar access")]
    public GoogleApiCredentials Credentials { get; set; }

    [Tooltip("URL of the local Google Calendar API proxy, e.g. http://127.0.0.1:5100")]
    public string LocalApiUrl { get; set; }
}
