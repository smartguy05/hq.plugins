using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Mailchimp.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Mailchimp API key (Account → Extras → API keys). The datacenter suffix, e.g. '-us21', is parsed from the key.")]
    public string ApiKey { get; set; }

    [Tooltip("Require user confirmation before sending a campaign. Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
