using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.DocuSign.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Integration Key (Client ID) from the DocuSign app")]
    public string IntegrationKey { get; set; }

    [Tooltip("API user GUID to impersonate (DocuSign Admin → Users → API Username)")]
    public string UserId { get; set; }

    [Tooltip("API Account ID (DocuSign Admin → Apps and Keys → My Account ID)")]
    public string AccountId { get; set; }

    [Sensitive]
    [Tooltip("RSA private key (PEM) for JWT grant. Requires one-time admin consent for the app.")]
    public string PrivateKey { get; set; }

    [Tooltip("API base path: https://demo.docusign.net for sandbox, https://www.docusign.net (or your account base URI) for production")]
    public string BasePath { get; set; } = "https://demo.docusign.net";

    [Tooltip("Require user confirmation before sending or voiding envelopes. Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
