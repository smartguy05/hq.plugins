using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Microsoft365.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Azure AD tenant ID from your App Registration, e.g. 12345678-abcd-1234-abcd-123456789abc")]
    public string TenantId { get; set; }

    [Tooltip("Application (client) ID from the Azure AD App Registration")]
    public string ClientId { get; set; }

    [Sensitive]
    [Tooltip("Client secret value from the Azure AD App Registration")]
    public string ClientSecret { get; set; }

    [Tooltip("Default drive ID to operate on when a request omits one. Required for app-only access — find via the SharePoint site or user's drive. App registration needs Files.ReadWrite.All and Sites.ReadWrite.All application permissions.")]
    public string DefaultDriveId { get; set; }
}
