using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Asana.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Asana Personal Access Token. Create at https://app.asana.com/0/my-apps")]
    public string AccessToken { get; set; }

    [Tooltip("Asana API base URL. Override only for testing or proxy setups.")]
    public string BaseUrl { get; set; } = "https://app.asana.com/api/1.0";
}
