using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.HomeAssistantVoice.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Long-lived access token from Home Assistant. Generate at /profile under Long-Lived Access Tokens.")]
    public string HomeAssistApiKey { get; set; }

    [Tooltip("Base URL of your Home Assistant instance, e.g. http://192.168.1.100:8123")]
    public string HomeAssistUrl { get; set; }
}
