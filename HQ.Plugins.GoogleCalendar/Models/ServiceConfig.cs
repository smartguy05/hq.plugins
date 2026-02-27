using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleCalendar.Models;

public record ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public GoogleApiCredentials Credentials { get; set; }
    public string LocalApiUrl { get; set; }
}