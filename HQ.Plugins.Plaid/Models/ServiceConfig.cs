using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Plaid.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Plaid client_id (Plaid Dashboard → Keys)")]
    public string ClientId { get; set; }

    [Sensitive]
    [Tooltip("Plaid secret for the selected environment")]
    public string Secret { get; set; }

    [Sensitive]
    [Tooltip("Per-item access_token obtained from the Plaid Link setup flow (one bank connection)")]
    public string AccessToken { get; set; }

    [Tooltip("Plaid environment: sandbox | production (default sandbox)")]
    public string Environment { get; set; }
}
