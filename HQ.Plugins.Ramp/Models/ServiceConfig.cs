using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Ramp.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Ramp OAuth client ID (Developer → API)")]
    public string ClientId { get; set; }

    [Sensitive]
    [Tooltip("Ramp OAuth client secret")]
    public string ClientSecret { get; set; }

    [Tooltip("Space-separated OAuth scopes to request, e.g. 'transactions:read cards:read users:read'")]
    public string Scopes { get; set; } = "transactions:read cards:read reimbursements:read users:read departments:read limits:read";

    [Tooltip("Use the Ramp sandbox (demo-api.ramp.com) instead of production. Default true for development.")]
    public bool UseSandbox { get; set; } = true;
}
