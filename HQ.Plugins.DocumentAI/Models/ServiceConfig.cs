using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.DocumentAI.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google OAuth 2.0 credentials with the cloud-platform scope")]
    public GoogleApiCredentials Credentials { get; set; }

    [Tooltip("Google Cloud project id (for Document AI)")]
    public string ProjectId { get; set; }

    [Tooltip("Document AI processor location, e.g. 'us' or 'eu' (default us)")]
    public string Location { get; set; }

    [Tooltip("Document AI processor id for receipts (Expense/Receipt parser)")]
    public string ReceiptProcessorId { get; set; }

    [Tooltip("Document AI processor id for general document/form parsing")]
    public string DocumentProcessorId { get; set; }
}
