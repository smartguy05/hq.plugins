using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.DocumentAI.Models;

namespace HQ.Plugins.DocumentAI;

/// <summary>
/// Extract text and structured fields from documents. Plain OCR uses Cloud Vision; receipts and
/// form fields use Document AI processors. Auth reuses the Google refresh-token credential pattern
/// (cloud-platform scope), fetching a bearer token per request.
/// </summary>
public class DocumentAiService
{
    private const string VisionUrl = "https://vision.googleapis.com/v1/images:annotate";
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/cloud-platform"];

    private readonly LogDelegate _logger;

    public DocumentAiService(LogDelegate logger) => _logger = logger;

    /// <summary>Build the Document AI process endpoint URL for a processor.</summary>
    public static string ProcessorUrl(string location, string projectId, string processorId)
    {
        var loc = string.IsNullOrWhiteSpace(location) ? "us" : location.Trim();
        return $"https://{loc}-documentai.googleapis.com/v1/projects/{projectId}/locations/{loc}/processors/{processorId}:process";
    }

    private static async Task<string> AccessToken(ServiceConfig config)
    {
        var creds = config.Credentials ?? throw new InvalidOperationException("Document AI credentials are not configured.");
        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret },
            Scopes = Scopes
        });
        var credential = new UserCredential(flow, creds.GoogleUser ?? "user", new TokenResponse { RefreshToken = creds.RefreshToken });
        return await credential.GetAccessTokenForRequestAsync();
    }

    [Display(Name = DocumentAiMethods.ExtractText)]
    [Description("Extract all text (OCR) from an image or PDF. Provide base64 content or an image URI.")]
    [Parameters(typeof(ExtractTextArgs))]
    public Task<object> ExtractText(ServiceConfig config, ExtractTextArgs r) =>
        Guard(async () =>
        {
            if (string.IsNullOrWhiteSpace(r.Content) && string.IsNullOrWhiteSpace(r.ImageUri))
                return new { Success = false, Error = "Provide content (base64) or imageUri." };

            using var client = new DocumentAiClient(await AccessToken(config));
            var image = string.IsNullOrWhiteSpace(r.Content)
                ? new JsonObject { ["source"] = new JsonObject { ["imageUri"] = r.ImageUri } }
                : new JsonObject { ["content"] = r.Content };
            var body = new JsonObject
            {
                ["requests"] = new JsonArray(new JsonObject
                {
                    ["image"] = image,
                    ["features"] = new JsonArray(new JsonObject { ["type"] = "DOCUMENT_TEXT_DETECTION" })
                })
            };
            var doc = await client.PostAsync(VisionUrl, body);
            var text = doc.TryGetProperty("responses", out var resps) && resps.GetArrayLength() > 0 &&
                       resps[0].TryGetProperty("fullTextAnnotation", out var fta) &&
                       fta.TryGetProperty("text", out var t)
                ? t.GetString() : "";
            return new { Success = true, Text = text };
        });

    [Display(Name = DocumentAiMethods.ExtractReceipt)]
    [Description("Extract structured fields from a receipt (merchant, total, line items, date) using the configured receipt processor.")]
    [Parameters(typeof(ExtractReceiptArgs))]
    public Task<object> ExtractReceipt(ServiceConfig config, ExtractReceiptArgs r) =>
        ProcessDocument(config, r.Content, r.MimeType, config.ReceiptProcessorId, "receipt");

    [Display(Name = DocumentAiMethods.ExtractDocumentFields)]
    [Description("Extract text and form fields from a general document using the configured document processor.")]
    [Parameters(typeof(ExtractDocumentFieldsArgs))]
    public Task<object> ExtractDocumentFields(ServiceConfig config, ExtractDocumentFieldsArgs r) =>
        ProcessDocument(config, r.Content, r.MimeType, config.DocumentProcessorId, "document");

    private Task<object> ProcessDocument(ServiceConfig config, string content, string mimeType, string processorId, string kind) =>
        Guard(async () =>
        {
            if (string.IsNullOrWhiteSpace(content)) return new { Success = false, Error = "content (base64) is required." };
            if (string.IsNullOrWhiteSpace(processorId)) return new { Success = false, Error = $"No {kind} processor id configured." };
            if (string.IsNullOrWhiteSpace(config.ProjectId)) return new { Success = false, Error = "ProjectId is not configured." };

            using var client = new DocumentAiClient(await AccessToken(config));
            var url = ProcessorUrl(config.Location, config.ProjectId, processorId);
            var body = new JsonObject
            {
                ["rawDocument"] = new JsonObject
                {
                    ["content"] = content,
                    ["mimeType"] = string.IsNullOrWhiteSpace(mimeType) ? "application/pdf" : mimeType
                }
            };
            var resp = await client.PostAsync(url, body);
            var document = resp.TryGetProperty("document", out var d) ? d : resp;
            object text = document.TryGetProperty("text", out var t) ? t.GetString() : null;
            object entities = document.TryGetProperty("entities", out var e) ? e : (object)Array.Empty<object>();
            return new { Success = true, Text = text, Entities = entities };
        });

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Document AI operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
