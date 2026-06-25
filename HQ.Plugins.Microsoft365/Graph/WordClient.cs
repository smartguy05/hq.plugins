using HQ.Plugins.Microsoft365.Models;
using Microsoft.Graph;

namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>
/// Word document operations. Graph has no live structured-edit API for Word (unlike Google
/// Docs), so v1 supports create-from-text and read-as-text only.
/// </summary>
public class WordClient
{
    private const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly GraphServiceClient _graph;
    private readonly string _defaultDriveId;

    public WordClient(ServiceConfig config)
    {
        _graph = GraphClientFactory.CreateGraph(config);
        _defaultDriveId = config.DefaultDriveId;
    }

    private string DriveId(string requestDriveId)
    {
        var id = string.IsNullOrWhiteSpace(requestDriveId) ? _defaultDriveId : requestDriveId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("driveId is required (or set DefaultDriveId in the plugin config).");
        return id;
    }

    public async Task<object> Create(WordCreateArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return new { Success = false, Error = "name is required" };
        var driveId = DriveId(r.DriveId);
        var parentId = string.IsNullOrWhiteSpace(r.ItemId) ? "root" : r.ItemId;

        var fileName = r.Name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) ? r.Name : r.Name + ".docx";
        var bytes = DocxHelper.CreateDocx(r.Text ?? "");

        using var stream = new MemoryStream(bytes);
        var item = await _graph.Drives[driveId].Items[parentId].ItemWithPath(fileName).Content.PutAsync(stream);
        return new { Success = true, ItemId = item?.Id, FileName = item?.Name, item?.WebUrl, MimeType = DocxMime };
    }

    public async Task<object> Read(WordReadArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId) && string.IsNullOrWhiteSpace(r.Path))
            return new { Success = false, Error = "itemId or path is required" };
        var driveId = DriveId(r.DriveId);

        string itemId = r.ItemId;
        if (string.IsNullOrWhiteSpace(itemId))
        {
            var byPath = await _graph.Drives[driveId].Items["root"].ItemWithPath(r.Path).GetAsync();
            itemId = byPath?.Id ?? throw new InvalidOperationException($"No item at path '{r.Path}'.");
        }

        var meta = await _graph.Drives[driveId].Items[itemId].GetAsync();
        var stream = await _graph.Drives[driveId].Items[itemId].Content.GetAsync();
        if (stream is null) return new { Success = false, Error = "File content stream was null" };

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var text = DocxHelper.ExtractText(ms.ToArray());
        return new { Success = true, ItemId = itemId, FileName = meta?.Name, Text = text };
    }
}
