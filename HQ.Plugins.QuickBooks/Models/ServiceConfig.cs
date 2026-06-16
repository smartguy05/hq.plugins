using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.QuickBooks.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Intuit OAuth 2.0 credentials for QuickBooks Online")]
    public IntuitCredentials Credentials { get; set; }

    [Tooltip("QuickBooks company (Realm) ID — returned alongside the OAuth grant")]
    public string RealmId { get; set; }

    [Tooltip("Use the Intuit sandbox environment instead of production. Default true for development.")]
    public bool UseSandbox { get; set; } = true;

    [Tooltip("Require user confirmation before write actions (create/send invoice, expense, bill, customer). Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
