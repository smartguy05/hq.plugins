using HQ.Plugins.Microsoft365.Models;
using Microsoft.Graph;
using Microsoft.Graph.Drives.Item.Items.Item.CreateLink;
using Microsoft.Graph.Models;

namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>OneDrive / SharePoint file operations (storage surface) via Microsoft Graph.</summary>
public class FilesClient
{
    private readonly GraphServiceClient _graph;
    private readonly string _defaultDriveId;

    public FilesClient(ServiceConfig config)
    {
        _graph = GraphClientFactory.CreateGraph(config);
        _defaultDriveId = config.DefaultDriveId;
    }

    private string DriveId(ServiceRequest r)
    {
        var id = string.IsNullOrWhiteSpace(r.DriveId) ? _defaultDriveId : r.DriveId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("driveId is required (or set DefaultDriveId in the plugin config).");
        return id;
    }

    // Resolve the target item id, supporting either an explicit itemId, a path, or root.
    private async Task<string> ResolveItemId(string driveId, ServiceRequest r)
    {
        if (!string.IsNullOrWhiteSpace(r.ItemId)) return r.ItemId;
        if (!string.IsNullOrWhiteSpace(r.Path))
        {
            var item = await _graph.Drives[driveId].Items["root"].ItemWithPath(r.Path).GetAsync();
            return item?.Id ?? throw new InvalidOperationException($"No item found at path '{r.Path}'.");
        }
        return "root";
    }

    public async Task<object> List(ServiceRequest r)
    {
        var driveId = DriveId(r);
        var itemId = await ResolveItemId(driveId, r);
        var children = await _graph.Drives[driveId].Items[itemId].Children.GetAsync(rc =>
        {
            rc.QueryParameters.Top = r.PageSize is > 0 and <= 999 ? r.PageSize.Value : 100;
        });
        return new { Success = true, Items = children?.Value?.Select(MapItem) ?? [] };
    }

    public async Task<object> Search(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Query)) return new { Success = false, Error = "query is required" };
        var driveId = DriveId(r);
        var results = await _graph.Drives[driveId].Items["root"].SearchWithQ(r.Query).GetAsSearchWithQGetResponseAsync(rc =>
        {
            rc.QueryParameters.Top = r.PageSize is > 0 and <= 999 ? r.PageSize.Value : 50;
        });
        return new { Success = true, Items = results?.Value?.Select(MapItem) ?? [] };
    }

    public async Task<object> Get(ServiceRequest r)
    {
        var driveId = DriveId(r);
        var itemId = await ResolveItemId(driveId, r);
        var item = await _graph.Drives[driveId].Items[itemId].GetAsync();
        return new { Success = true, Item = MapItem(item) };
    }

    public async Task<object> Download(ServiceRequest r)
    {
        var driveId = DriveId(r);
        var itemId = await ResolveItemId(driveId, r);
        var item = await _graph.Drives[driveId].Items[itemId].GetAsync();
        var stream = await _graph.Drives[driveId].Items[itemId].Content.GetAsync();
        if (stream is null) return new { Success = false, Error = "File content stream was null" };

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return new
        {
            Success = true,
            ItemId = item?.Id,
            FileName = item?.Name,
            MimeType = item?.File?.MimeType,
            Content = Convert.ToBase64String(ms.ToArray())
        };
    }

    public async Task<object> Upload(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return new { Success = false, Error = "name is required" };
        if (string.IsNullOrWhiteSpace(r.Content)) return new { Success = false, Error = "content (base64) is required" };

        var driveId = DriveId(r);
        // Upload into the resolved folder (itemId/path → folder, default root).
        var parentId = await ResolveItemId(driveId, r);

        var bytes = Convert.FromBase64String(r.Content);
        using var stream = new MemoryStream(bytes);
        var uploaded = await _graph.Drives[driveId].Items[parentId].ItemWithPath(r.Name).Content.PutAsync(stream);
        return new { Success = true, Item = MapItem(uploaded) };
    }

    public async Task<object> CreateFolder(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return new { Success = false, Error = "name is required" };
        var driveId = DriveId(r);
        var parentId = await ResolveItemId(driveId, r);

        var folder = await _graph.Drives[driveId].Items[parentId].Children.PostAsync(new DriveItem
        {
            Name = r.Name,
            Folder = new Folder(),
            AdditionalData = new Dictionary<string, object> { ["@microsoft.graph.conflictBehavior"] = "rename" }
        });
        return new { Success = true, Item = MapItem(folder) };
    }

    public async Task<object> Move(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId)) return new { Success = false, Error = "itemId is required" };
        if (string.IsNullOrWhiteSpace(r.DestinationFolderId)) return new { Success = false, Error = "destinationFolderId is required" };
        var driveId = DriveId(r);

        var patch = new DriveItem { ParentReference = new ItemReference { Id = r.DestinationFolderId } };
        if (!string.IsNullOrWhiteSpace(r.Name)) patch.Name = r.Name;
        var moved = await _graph.Drives[driveId].Items[r.ItemId].PatchAsync(patch);
        return new { Success = true, Item = MapItem(moved) };
    }

    public async Task<object> Copy(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId)) return new { Success = false, Error = "itemId is required" };
        var driveId = DriveId(r);

        var body = new Microsoft.Graph.Drives.Item.Items.Item.Copy.CopyPostRequestBody();
        if (!string.IsNullOrWhiteSpace(r.Name)) body.Name = r.Name;
        if (!string.IsNullOrWhiteSpace(r.DestinationFolderId))
            body.ParentReference = new ItemReference { DriveId = driveId, Id = r.DestinationFolderId };

        // Copy is a long-running operation; Graph returns 202 Accepted with a monitor URL.
        await _graph.Drives[driveId].Items[r.ItemId].Copy.PostAsync(body);
        return new { Success = true, ItemId = r.ItemId, Status = "Copy accepted (asynchronous)" };
    }

    public async Task<object> Delete(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId)) return new { Success = false, Error = "itemId is required" };
        var driveId = DriveId(r);
        await _graph.Drives[driveId].Items[r.ItemId].DeleteAsync();
        return new { Success = true, ItemId = r.ItemId, Deleted = true };
    }

    public async Task<object> Share(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId)) return new { Success = false, Error = "itemId is required" };
        var driveId = DriveId(r);

        var link = await _graph.Drives[driveId].Items[r.ItemId].CreateLink.PostAsync(new CreateLinkPostRequestBody
        {
            Type = string.IsNullOrWhiteSpace(r.LinkType) ? "view" : r.LinkType,
            Scope = string.IsNullOrWhiteSpace(r.Scope) ? "anonymous" : r.Scope
        });
        return new { Success = true, ItemId = r.ItemId, WebUrl = link?.Link?.WebUrl, Type = link?.Link?.Type, Scope = link?.Link?.Scope };
    }

    private static object MapItem(DriveItem i) => i is null ? null : new
    {
        i.Id,
        i.Name,
        IsFolder = i.Folder is not null,
        Size = i.Size,
        MimeType = i.File?.MimeType,
        i.WebUrl,
        LastModified = i.LastModifiedDateTime
    };
}
