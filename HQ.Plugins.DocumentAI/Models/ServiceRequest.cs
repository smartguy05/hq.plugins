using HQ.Models.Interfaces;

namespace HQ.Plugins.DocumentAI.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Document content as base64 (preferred), OR a public/GCS image URI for plain OCR.
    public string Content { get; set; }
    public string ImageUri { get; set; }

    // MIME type of Content, e.g. "application/pdf", "image/jpeg", "image/png".
    public string MimeType { get; set; }
}
