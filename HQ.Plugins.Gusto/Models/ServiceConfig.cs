using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Gusto.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Gusto OAuth 2.0 credentials")]
    public GustoCredentials Credentials { get; set; }

    [Tooltip("Use the Gusto demo environment (api.gusto-demo.com) instead of production. Default true for development.")]
    public bool UseDemo { get; set; } = true;

    [Tooltip("Require user confirmation before write actions (e.g. create time-off request). Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
