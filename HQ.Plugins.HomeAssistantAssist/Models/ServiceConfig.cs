using HQ.Models.Interfaces;

namespace HQ.Plugins.HomeAssistantVoice.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string HomeAssistApiKey { get; set; }
    public string HomeAssistUrl { get; set; }
}