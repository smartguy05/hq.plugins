namespace HQ.Plugins.DocuSign;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on DocuSignService.</summary>
public static class DocuSignMethods
{
    public const string SendEnvelope = "send_envelope";
    public const string SendEnvelopeFromTemplate = "send_envelope_from_template";
    public const string GetEnvelopeStatus = "get_envelope_status";
    public const string ListEnvelopes = "list_envelopes";
    public const string ListRecipients = "list_recipients";
    public const string DownloadCompletedDocument = "download_completed_document";
    public const string VoidEnvelope = "void_envelope";
}
