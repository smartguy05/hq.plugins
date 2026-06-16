using HQ.Models.Interfaces;

namespace HQ.Plugins.DocuSign.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Envelope identity
    public string EnvelopeId { get; set; }
    public string DocumentId { get; set; }   // for download (default "combined")

    // Send-from-document
    public string DocumentBase64 { get; set; }
    public string DocumentName { get; set; }
    public string Subject { get; set; }

    // Signer / template role
    public string SignerEmail { get; set; }
    public string SignerName { get; set; }
    public string TemplateId { get; set; }
    public string RoleName { get; set; }

    // Listing
    public string FromDate { get; set; }     // YYYY-MM-DD; default = 30 days ago
    public string Status { get; set; }       // filter: sent | completed | voided ...

    // Void
    public string Reason { get; set; }
}
