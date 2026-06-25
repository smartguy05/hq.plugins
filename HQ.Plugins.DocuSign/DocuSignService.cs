using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using DocuSign.eSign.Api;
using DocuSign.eSign.Client;
using DocuSign.eSign.Model;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.DocuSign.Models;

namespace HQ.Plugins.DocuSign;

/// <summary>
/// Tool surface for DocuSign e-signature. Authenticates via OAuth JWT grant (server-to-server).
/// Sending and voiding envelopes are outward-facing and route through the HQ confirmation flow.
/// </summary>
public class DocuSignService
{
    private const string PluginName = "DocuSign";
    private static readonly List<string> Scopes = ["signature", "impersonation"];

    private readonly LogDelegate _logger;
    private readonly INotificationService _notificationService;

    public DocuSignService(INotificationService notificationService, LogDelegate logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    private static EnvelopesApi BuildApi(ServiceConfig config)
    {
        var basePath = config.BasePath.TrimEnd('/');
        if (!basePath.EndsWith("/restapi")) basePath += "/restapi";
        var oauthBase = config.BasePath.Contains("demo", StringComparison.OrdinalIgnoreCase)
            ? "account-d.docusign.com"
            : "account.docusign.com";

        var apiClient = new ApiClient(basePath);
        var keyBytes = Encoding.UTF8.GetBytes(config.PrivateKey ?? "");
        var token = apiClient.RequestJWTUserToken(config.IntegrationKey, config.UserId, oauthBase, keyBytes, 1, Scopes);
        apiClient.Configuration.DefaultHeader["Authorization"] = "Bearer " + token.access_token;
        return new EnvelopesApi(apiClient);
    }

    [Display(Name = DocuSignMethods.SendEnvelope)]
    [Description("Send a document (base64 PDF) to a signer for signature.")]
    [Parameters(typeof(SendEnvelopeArgs))]
    [SupportsConfirmation]
    public Task<object> SendEnvelope(ServiceConfig config, SendEnvelopeArgs r) =>
        Guard(() => Confirm(config, r, "Send this document for signature?", $"To {r.SignerName} <{r.SignerEmail}>", async () =>
        {
            var api = BuildApi(config);
            var document = new Document
            {
                DocumentBase64 = r.DocumentBase64,
                Name = string.IsNullOrWhiteSpace(r.DocumentName) ? "Document" : r.DocumentName,
                FileExtension = "pdf",
                DocumentId = "1"
            };
            var signer = new Signer
            {
                Email = r.SignerEmail,
                Name = r.SignerName,
                RecipientId = "1",
                Tabs = new Tabs
                {
                    SignHereTabs = [new SignHere { DocumentId = "1", PageNumber = "1", XPosition = "100", YPosition = "100" }]
                }
            };
            var definition = new EnvelopeDefinition
            {
                EmailSubject = string.IsNullOrWhiteSpace(r.Subject) ? "Please sign" : r.Subject,
                Documents = [document],
                Recipients = new Recipients { Signers = [signer] },
                Status = "sent"
            };
            var summary = await api.CreateEnvelopeAsync(config.AccountId, definition);
            return new { Success = true, summary.EnvelopeId, summary.Status };
        }));

    [Display(Name = DocuSignMethods.SendEnvelopeFromTemplate)]
    [Description("Send an envelope from an existing template, filling one recipient role.")]
    [Parameters(typeof(SendEnvelopeFromTemplateArgs))]
    [SupportsConfirmation]
    public Task<object> SendEnvelopeFromTemplate(ServiceConfig config, SendEnvelopeFromTemplateArgs r) =>
        Guard(() => Confirm(config, r, "Send this template for signature?", $"Template {r.TemplateId} to {r.SignerEmail}", async () =>
        {
            var api = BuildApi(config);
            var definition = new EnvelopeDefinition
            {
                TemplateId = r.TemplateId,
                EmailSubject = string.IsNullOrWhiteSpace(r.Subject) ? "Please sign" : r.Subject,
                TemplateRoles = [new TemplateRole { Email = r.SignerEmail, Name = r.SignerName, RoleName = r.RoleName }],
                Status = "sent"
            };
            var summary = await api.CreateEnvelopeAsync(config.AccountId, definition);
            return new { Success = true, summary.EnvelopeId, summary.Status };
        }));

    [Display(Name = DocuSignMethods.GetEnvelopeStatus)]
    [Description("Get the current status of an envelope.")]
    [Parameters(typeof(GetEnvelopeStatusArgs))]
    public Task<object> GetEnvelopeStatus(ServiceConfig config, GetEnvelopeStatusArgs r) =>
        Guard(async () =>
        {
            var api = BuildApi(config);
            var env = await api.GetEnvelopeAsync(config.AccountId, r.EnvelopeId);
            return new { Success = true, env.EnvelopeId, env.Status, env.EmailSubject, env.SentDateTime, env.CompletedDateTime };
        });

    [Display(Name = DocuSignMethods.ListEnvelopes)]
    [Description("List envelopes changed since a date (default last 30 days), optionally filtered by status.")]
    [Parameters(typeof(ListEnvelopesArgs))]
    public Task<object> ListEnvelopes(ServiceConfig config, ListEnvelopesArgs r) =>
        Guard(async () =>
        {
            var api = BuildApi(config);
            var options = new EnvelopesApi.ListStatusChangesOptions
            {
                fromDate = string.IsNullOrWhiteSpace(r.FromDate) ? DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd") : r.FromDate
            };
            if (!string.IsNullOrWhiteSpace(r.Status)) options.status = r.Status;
            var info = await api.ListStatusChangesAsync(config.AccountId, options);
            return new { Success = true, Envelopes = info.Envelopes?.Select(e => new { e.EnvelopeId, e.Status, e.EmailSubject, e.SentDateTime }) ?? [] };
        });

    [Display(Name = DocuSignMethods.ListRecipients)]
    [Description("List the recipients of an envelope and their signing status.")]
    [Parameters(typeof(ListRecipientsArgs))]
    public Task<object> ListRecipients(ServiceConfig config, ListRecipientsArgs r) =>
        Guard(async () =>
        {
            var api = BuildApi(config);
            var recipients = await api.ListRecipientsAsync(config.AccountId, r.EnvelopeId);
            return new { Success = true, Signers = recipients.Signers?.Select(s => new { s.Name, s.Email, s.RecipientId, s.Status }) ?? [] };
        });

    [Display(Name = DocuSignMethods.DownloadCompletedDocument)]
    [Description("Download an envelope's document(s) as base64. documentId defaults to 'combined' (all docs + certificate).")]
    [Parameters(typeof(DownloadCompletedDocumentArgs))]
    public Task<object> DownloadCompletedDocument(ServiceConfig config, DownloadCompletedDocumentArgs r) =>
        Guard(async () =>
        {
            var api = BuildApi(config);
            var documentId = string.IsNullOrWhiteSpace(r.DocumentId) ? "combined" : r.DocumentId;
            var stream = await api.GetDocumentAsync(config.AccountId, r.EnvelopeId, documentId);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return new { Success = true, r.EnvelopeId, DocumentId = documentId, Content = Convert.ToBase64String(ms.ToArray()) };
        });

    [Display(Name = DocuSignMethods.VoidEnvelope)]
    [Description("Void an in-progress envelope so it can no longer be signed.")]
    [Parameters(typeof(VoidEnvelopeArgs))]
    [SupportsConfirmation]
    public Task<object> VoidEnvelope(ServiceConfig config, VoidEnvelopeArgs r) =>
        Guard(() => Confirm(config, r, "Void this envelope?", $"Envelope {r.EnvelopeId}", async () =>
        {
            var api = BuildApi(config);
            var update = new Envelope { Status = "voided", VoidedReason = string.IsNullOrWhiteSpace(r.Reason) ? "Voided via HQ" : r.Reason };
            await api.UpdateAsync(config.AccountId, r.EnvelopeId, update);
            return new { Success = true, r.EnvelopeId, Status = "voided" };
        }));

    // ───────────────────────────── Plumbing ─────────────────────────────

    private async Task<object> Confirm(ServiceConfig config, IPluginServiceRequest request, string message, string content, Func<Task<object>> execute)
    {
        if (config.RequiresConfirmation && _notificationService != null)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmationId))
            {
                var confirmation = new Confirmation
                {
                    ConfirmationMessage = message,
                    Content = content,
                    Options = new Dictionary<string, bool> { { "Yes", true }, { "No", false } },
                    Id = Guid.NewGuid()
                };
                return await _notificationService.RequestConfirmation(PluginName, confirmation, request);
            }

            if (!_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
                return new { Success = false, Error = "Action was not confirmed." };
        }

        return await execute();
    }

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException ex)
        {
            await _logger(LogLevel.Error, $"DocuSign API error: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"DocuSign operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
