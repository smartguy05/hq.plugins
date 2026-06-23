using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.DocuSign.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable). Confirmation tools implement
/// <see cref="IPluginServiceRequest"/> so the request survives the confirmation replay round-trip.
/// </summary>

public class SendEnvelopeArgs : IPluginServiceRequest
{
    // Framework envelope fields — supplied by the orchestrator, hidden from the LLM schema,
    // preserved across the confirmation replay.
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("Base64-encoded PDF to sign")]
    public string DocumentBase64 { get; set; }

    [Description("Document name")]
    public string DocumentName { get; set; }

    [Description("Email subject")]
    public string Subject { get; set; }

    [Required]
    public string SignerEmail { get; set; }

    [Required]
    public string SignerName { get; set; }
}

public class SendEnvelopeFromTemplateArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required]
    public string TemplateId { get; set; }

    [Required, Description("Template role to fill")]
    public string RoleName { get; set; }

    [Required]
    public string SignerEmail { get; set; }

    [Required]
    public string SignerName { get; set; }

    public string Subject { get; set; }
}

public class GetEnvelopeStatusArgs
{
    [Required]
    public string EnvelopeId { get; set; }
}

public class ListEnvelopesArgs
{
    [Description("YYYY-MM-DD (default 30 days ago)")]
    public string FromDate { get; set; }

    [Description("Filter: sent | delivered | completed | voided | declined")]
    public string Status { get; set; }
}

public class ListRecipientsArgs
{
    [Required]
    public string EnvelopeId { get; set; }
}

public class DownloadCompletedDocumentArgs
{
    [Required]
    public string EnvelopeId { get; set; }

    [Description("Document ID, or 'combined' (default)")]
    public string DocumentId { get; set; }
}

public class VoidEnvelopeArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required]
    public string EnvelopeId { get; set; }

    [Description("Reason shown to recipients")]
    public string Reason { get; set; }
}
