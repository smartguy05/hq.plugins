using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Stripe.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Stripe secret API key (sk_test_… for development, sk_live_… for production)")]
    public string ApiKey { get; set; }

    [Tooltip("Require user confirmation before money-moving actions (create/send invoice, payment link, refund). Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
