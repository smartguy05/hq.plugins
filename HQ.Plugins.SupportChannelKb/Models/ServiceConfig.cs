using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.SupportChannelKb.Models;

public record ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Base URL of the Support Channel KB service, e.g. http://127.0.0.1:5200")]
    public string SupportChannelKbUrl { get; set; }

    [Tooltip("Default channel name to save new KB articles to")]
    public string DefaultSaveChannel { get; set; }

    [Tooltip("API key for authenticating with the Support Channel KB service")]
    public string DefaultChannelApiKey { get; set; }
}
