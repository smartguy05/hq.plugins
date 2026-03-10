using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleCalendar.Models;

public record ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google OAuth 2.0 credentials for calendar access")]
    public GoogleApiCredentials Credentials { get; set; }
}
