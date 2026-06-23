using System.Text;
using Google.Apis.Docs.v1;
using Google.Apis.Docs.v1.Data;
using HQ.Plugins.GoogleWorkspace.Models;

namespace HQ.Plugins.GoogleWorkspace.Clients;

/// <summary>Google Docs operations (document surface).</summary>
public class DocsClient
{
    private readonly DocsService _docs;

    public DocsClient(ServiceConfig config) => _docs = GoogleClientFactory.CreateDocs(config);

    public async Task<object> Create(DocsCreateArgs r)
    {
        var doc = await _docs.Documents.Create(new Document { Title = r.Title ?? r.Name ?? "Untitled document" }).ExecuteAsync();

        if (!string.IsNullOrEmpty(r.Text))
        {
            var batch = new BatchUpdateDocumentRequest
            {
                Requests =
                [
                    new Request
                    {
                        InsertText = new InsertTextRequest
                        {
                            Text = r.Text,
                            Location = new Location { Index = 1 }
                        }
                    }
                ]
            };
            await _docs.Documents.BatchUpdate(batch, doc.DocumentId).ExecuteAsync();
        }

        return new
        {
            Success = true,
            DocumentId = doc.DocumentId,
            doc.Title,
            WebViewLink = $"https://docs.google.com/document/d/{doc.DocumentId}/edit"
        };
    }

    public async Task<object> GetText(DocsGetTextArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (documentId) is required" };
        var doc = await _docs.Documents.Get(r.FileId).ExecuteAsync();
        return new { Success = true, DocumentId = doc.DocumentId, doc.Title, Text = ExtractText(doc.Body) };
    }

    public async Task<object> AppendText(DocsAppendTextArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (documentId) is required" };
        if (string.IsNullOrEmpty(r.Text)) return new { Success = false, Error = "text is required" };

        var doc = await _docs.Documents.Get(r.FileId).ExecuteAsync();
        var endIndex = GetEndIndex(doc.Body);
        // The body's trailing newline occupies the final index; insert just before it.
        var insertAt = Math.Max(1, endIndex - 1);

        var batch = new BatchUpdateDocumentRequest
        {
            Requests =
            [
                new Request
                {
                    InsertText = new InsertTextRequest
                    {
                        Text = r.Text,
                        Location = new Location { Index = insertAt }
                    }
                }
            ]
        };
        await _docs.Documents.BatchUpdate(batch, r.FileId).ExecuteAsync();
        return new { Success = true, DocumentId = r.FileId, InsertedAt = insertAt };
    }

    public async Task<object> ReplaceText(DocsReplaceTextArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (documentId) is required" };
        if (string.IsNullOrEmpty(r.Find)) return new { Success = false, Error = "find is required" };

        var batch = new BatchUpdateDocumentRequest
        {
            Requests =
            [
                new Request
                {
                    ReplaceAllText = new ReplaceAllTextRequest
                    {
                        ContainsText = new SubstringMatchCriteria { Text = r.Find, MatchCase = r.MatchCase ?? false },
                        ReplaceText = r.Replace ?? ""
                    }
                }
            ]
        };
        var result = await _docs.Documents.BatchUpdate(batch, r.FileId).ExecuteAsync();
        var occurrences = result.Replies?.FirstOrDefault()?.ReplaceAllText?.OccurrencesChanged ?? 0;
        return new { Success = true, DocumentId = r.FileId, OccurrencesChanged = occurrences };
    }

    /// <summary>Flattens a document body to plain text. Public for unit testing.</summary>
    public static string ExtractText(Body body)
    {
        if (body?.Content is null) return "";
        var sb = new StringBuilder();
        foreach (var element in body.Content)
        {
            if (element.Paragraph?.Elements is null) continue;
            foreach (var pe in element.Paragraph.Elements)
                if (pe.TextRun?.Content is not null) sb.Append(pe.TextRun.Content);
        }
        return sb.ToString();
    }

    private static int GetEndIndex(Body body)
    {
        if (body?.Content is null || body.Content.Count == 0) return 1;
        return body.Content.LastOrDefault(c => c.EndIndex.HasValue)?.EndIndex ?? 1;
    }
}
