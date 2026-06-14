using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Shopify.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Shop domain, e.g. my-store.myshopify.com")]
    public string ShopDomain { get; set; }

    [Sensitive]
    [Tooltip("Admin API access token (custom app → Admin API access token, starts with shpat_)")]
    public string AccessToken { get; set; }

    [Tooltip("Admin API version (default 2025-01)")]
    public string ApiVersion { get; set; } = "2025-01";

    [Tooltip("Require user confirmation before write actions (create product, update inventory, fulfill order, create draft order). Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
