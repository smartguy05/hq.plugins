using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using HQ.Plugins.GoogleWorkspace.Models;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace HQ.Plugins.GoogleWorkspace.Clients;

/// <summary>Google Drive operations (storage surface).</summary>
public class DriveClient
{
    private const string FileFields = "id,name,mimeType,size,modifiedTime,webViewLink,parents";
    private readonly DriveService _drive;

    public DriveClient(ServiceConfig config) => _drive = GoogleClientFactory.CreateDrive(config);

    public async Task<object> ListFiles(DriveListFilesArgs r)
    {
        var list = _drive.Files.List();
        var clauses = new List<string> { "trashed = false" };
        if (!string.IsNullOrWhiteSpace(r.FolderId)) clauses.Add($"'{r.FolderId}' in parents");
        if (!string.IsNullOrWhiteSpace(r.Query)) clauses.Add(r.Query);
        list.Q = string.Join(" and ", clauses);
        list.PageSize = r.PageSize is > 0 and <= 1000 ? r.PageSize.Value : 100;
        list.Fields = $"files({FileFields}),nextPageToken";
        list.OrderBy = string.IsNullOrWhiteSpace(r.OrderBy) ? "folder,modifiedTime desc" : r.OrderBy;

        var result = await list.ExecuteAsync();
        return new
        {
            Success = true,
            Files = result.Files?.Select(MapFile) ?? [],
            NextPageToken = result.NextPageToken
        };
    }

    public async Task<object> SearchFiles(DriveSearchFilesArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.Query))
            return new { Success = false, Error = "query is required" };

        var list = _drive.Files.List();
        var escaped = r.Query.Replace("'", "\\'");
        list.Q = $"fullText contains '{escaped}' and trashed = false";
        list.PageSize = r.PageSize is > 0 and <= 1000 ? r.PageSize.Value : 50;
        list.Fields = $"files({FileFields}),nextPageToken";

        var result = await list.ExecuteAsync();
        return new
        {
            Success = true,
            Files = result.Files?.Select(MapFile) ?? [],
            NextPageToken = result.NextPageToken
        };
    }

    public async Task<object> GetFile(DriveGetFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId is required" };
        var get = _drive.Files.Get(r.FileId);
        get.Fields = FileFields;
        var file = await get.ExecuteAsync();
        return new { Success = true, File = MapFile(file) };
    }

    public async Task<object> DownloadFile(DriveDownloadFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId is required" };

        var get = _drive.Files.Get(r.FileId);
        get.Fields = FileFields;
        var meta = await get.ExecuteAsync();

        using var ms = new MemoryStream();
        string returnedMime;

        // Google-native types (Docs/Sheets/Slides) cannot be downloaded directly — they must
        // be exported to a concrete format. Caller may override via mimeType.
        if (meta.MimeType is not null && meta.MimeType.StartsWith("application/vnd.google-apps."))
        {
            var exportMime = string.IsNullOrWhiteSpace(r.MimeType) ? DefaultExportMime(meta.MimeType) : r.MimeType;
            await _drive.Files.Export(r.FileId, exportMime).DownloadAsync(ms);
            returnedMime = exportMime;
        }
        else
        {
            await get.DownloadAsync(ms);
            returnedMime = meta.MimeType;
        }

        return new
        {
            Success = true,
            FileId = r.FileId,
            FileName = meta.Name,
            MimeType = returnedMime,
            Content = Convert.ToBase64String(ms.ToArray())
        };
    }

    public async Task<object> UploadFile(DriveUploadFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return new { Success = false, Error = "name is required" };
        if (string.IsNullOrWhiteSpace(r.Content)) return new { Success = false, Error = "content (base64) is required" };

        var metadata = new DriveFile { Name = r.Name };
        if (!string.IsNullOrWhiteSpace(r.FolderId)) metadata.Parents = [r.FolderId];

        var bytes = Convert.FromBase64String(r.Content);
        using var stream = new MemoryStream(bytes);
        var contentType = string.IsNullOrWhiteSpace(r.MimeType) ? "application/octet-stream" : r.MimeType;

        var upload = _drive.Files.Create(metadata, stream, contentType);
        upload.Fields = FileFields;
        var progress = await upload.UploadAsync();
        if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            return new { Success = false, Error = progress.Exception?.Message ?? "Upload failed" };

        return new { Success = true, File = MapFile(upload.ResponseBody) };
    }

    public async Task<object> CreateFolder(DriveCreateFolderArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.Name)) return new { Success = false, Error = "name is required" };
        var metadata = new DriveFile { Name = r.Name, MimeType = "application/vnd.google-apps.folder" };
        if (!string.IsNullOrWhiteSpace(r.FolderId)) metadata.Parents = [r.FolderId];

        var create = _drive.Files.Create(metadata);
        create.Fields = FileFields;
        var folder = await create.ExecuteAsync();
        return new { Success = true, File = MapFile(folder) };
    }

    public async Task<object> MoveFile(DriveMoveFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId is required" };
        if (string.IsNullOrWhiteSpace(r.FolderId)) return new { Success = false, Error = "folderId (destination) is required" };

        var get = _drive.Files.Get(r.FileId);
        get.Fields = "parents";
        var existing = await get.ExecuteAsync();

        var update = _drive.Files.Update(new DriveFile(), r.FileId);
        update.AddParents = r.FolderId;
        if (existing.Parents is { Count: > 0 }) update.RemoveParents = string.Join(",", existing.Parents);
        update.Fields = FileFields;
        var file = await update.ExecuteAsync();
        return new { Success = true, File = MapFile(file) };
    }

    public async Task<object> CopyFile(DriveCopyFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId is required" };
        var metadata = new DriveFile();
        if (!string.IsNullOrWhiteSpace(r.Name)) metadata.Name = r.Name;
        if (!string.IsNullOrWhiteSpace(r.FolderId)) metadata.Parents = [r.FolderId];

        var copy = _drive.Files.Copy(metadata, r.FileId);
        copy.Fields = FileFields;
        var file = await copy.ExecuteAsync();
        return new { Success = true, File = MapFile(file) };
    }

    public async Task<object> DeleteFile(DriveDeleteFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId is required" };

        if (r.Permanent == true)
        {
            await _drive.Files.Delete(r.FileId).ExecuteAsync();
            return new { Success = true, FileId = r.FileId, Permanent = true };
        }

        await _drive.Files.Update(new DriveFile { Trashed = true }, r.FileId).ExecuteAsync();
        return new { Success = true, FileId = r.FileId, Trashed = true };
    }

    public async Task<object> ShareFile(DriveShareFileArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId is required" };

        var permission = new Permission
        {
            Role = string.IsNullOrWhiteSpace(r.Role) ? "reader" : r.Role,
            Type = string.IsNullOrWhiteSpace(r.Type) ? "anyone" : r.Type
        };
        if (!string.IsNullOrWhiteSpace(r.EmailAddress)) permission.EmailAddress = r.EmailAddress;

        await _drive.Permissions.Create(permission, r.FileId).ExecuteAsync();

        var get = _drive.Files.Get(r.FileId);
        get.Fields = "id,name,webViewLink";
        var file = await get.ExecuteAsync();
        return new { Success = true, FileId = r.FileId, file.WebViewLink, Role = permission.Role, Type = permission.Type };
    }

    /// <summary>Default export MIME for a Google-native file. Public for unit testing.</summary>
    public static string DefaultExportMime(string googleMimeType) => googleMimeType switch
    {
        "application/vnd.google-apps.document" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.google-apps.spreadsheet" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.google-apps.presentation" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.google-apps.drawing" => "image/png",
        _ => "application/pdf"
    };

    private static object MapFile(DriveFile f) => f is null ? null : new
    {
        f.Id,
        f.Name,
        f.MimeType,
        Size = f.Size,
        ModifiedTime = f.ModifiedTimeDateTimeOffset,
        f.WebViewLink,
        f.Parents
    };
}
