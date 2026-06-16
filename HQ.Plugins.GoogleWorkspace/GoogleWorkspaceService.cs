using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleWorkspace.Clients;
using HQ.Plugins.GoogleWorkspace.Models;

namespace HQ.Plugins.GoogleWorkspace;

/// <summary>
/// Tool surface for Google Workspace: Drive (files), Docs (documents), Sheets (spreadsheets).
/// Annotations are scanned by ServiceExtensions.GetServiceToolCalls; ProcessRequest dispatches
/// by matching [Display(Name)] to ServiceRequest.Method.
/// </summary>
public class GoogleWorkspaceService
{
    private readonly LogDelegate _logger;
    private readonly Lazy<DriveClient> _drive;
    private readonly Lazy<DocsClient> _docs;
    private readonly Lazy<SheetsClient> _sheets;

    public GoogleWorkspaceService(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _drive = new Lazy<DriveClient>(() => new DriveClient(config));
        _docs = new Lazy<DocsClient>(() => new DocsClient(config));
        _sheets = new Lazy<SheetsClient>(() => new SheetsClient(config));
    }

    // ───────────────────────────── Drive ─────────────────────────────

    [Display(Name = GoogleWorkspaceMethods.DriveListFiles)]
    [Description("List files and folders in Google Drive. Optionally scope to a folder or pass a raw Drive query.")]
    [Parameters("""{"type":"object","properties":{"folderId":{"type":"string","description":"Folder ID to list children of (omit for Drive root)"},"query":{"type":"string","description":"Optional raw Drive query clause, e.g. \"mimeType='application/pdf'\""},"pageSize":{"type":"integer","description":"Max results (1-1000, default 100)"},"orderBy":{"type":"string","description":"Sort order, e.g. 'modifiedTime desc'"}},"required":[]}""")]
    public Task<object> DriveListFiles(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.ListFiles(r));

    [Display(Name = GoogleWorkspaceMethods.DriveSearchFiles)]
    [Description("Full-text search across the user's Google Drive and return matching files.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Text to search for in file names and contents"},"pageSize":{"type":"integer","description":"Max results (1-1000, default 50)"}},"required":["query"]}""")]
    public Task<object> DriveSearchFiles(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.SearchFiles(r));

    [Display(Name = GoogleWorkspaceMethods.DriveGetFile)]
    [Description("Get metadata for a single Drive file (name, type, size, link, parents).")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The Drive file ID"}},"required":["fileId"]}""")]
    public Task<object> DriveGetFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.GetFile(r));

    [Display(Name = GoogleWorkspaceMethods.DriveDownloadFile)]
    [Description("Download a Drive file's contents as base64. Google-native files (Docs/Sheets/Slides) are exported; override the export format with mimeType.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The Drive file ID"},"mimeType":{"type":"string","description":"Export MIME type for Google-native files, e.g. 'application/pdf'"}},"required":["fileId"]}""")]
    public Task<object> DriveDownloadFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.DownloadFile(r));

    [Display(Name = GoogleWorkspaceMethods.DriveUploadFile)]
    [Description("Upload a new file to Drive from base64-encoded content.")]
    [Parameters("""{"type":"object","properties":{"name":{"type":"string","description":"File name"},"content":{"type":"string","description":"Base64-encoded file bytes"},"mimeType":{"type":"string","description":"MIME type of the file"},"folderId":{"type":"string","description":"Destination folder ID (omit for root)"}},"required":["name","content"]}""")]
    public Task<object> DriveUploadFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.UploadFile(r));

    [Display(Name = GoogleWorkspaceMethods.DriveCreateFolder)]
    [Description("Create a new folder in Drive.")]
    [Parameters("""{"type":"object","properties":{"name":{"type":"string","description":"Folder name"},"folderId":{"type":"string","description":"Parent folder ID (omit for root)"}},"required":["name"]}""")]
    public Task<object> DriveCreateFolder(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.CreateFolder(r));

    [Display(Name = GoogleWorkspaceMethods.DriveMoveFile)]
    [Description("Move a file to a different folder.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The file to move"},"folderId":{"type":"string","description":"Destination folder ID"}},"required":["fileId","folderId"]}""")]
    public Task<object> DriveMoveFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.MoveFile(r));

    [Display(Name = GoogleWorkspaceMethods.DriveCopyFile)]
    [Description("Create a copy of a Drive file, optionally with a new name and destination folder.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The file to copy"},"name":{"type":"string","description":"Name for the copy"},"folderId":{"type":"string","description":"Destination folder ID"}},"required":["fileId"]}""")]
    public Task<object> DriveCopyFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.CopyFile(r));

    [Display(Name = GoogleWorkspaceMethods.DriveDeleteFile)]
    [Description("Delete a Drive file. Moves it to trash by default; set permanent=true to delete forever.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The file to delete"},"permanent":{"type":"boolean","description":"Permanently delete instead of trashing"}},"required":["fileId"]}""")]
    public Task<object> DriveDeleteFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.DeleteFile(r));

    [Display(Name = GoogleWorkspaceMethods.DriveShareFile)]
    [Description("Share a Drive file by creating a permission and returning a shareable link.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The file to share"},"role":{"type":"string","description":"reader | writer | commenter | owner (default reader)"},"type":{"type":"string","description":"user | group | domain | anyone (default anyone)"},"emailAddress":{"type":"string","description":"Email for user/group grants"}},"required":["fileId"]}""")]
    public Task<object> DriveShareFile(ServiceConfig config, ServiceRequest r) => Guard(() => _drive.Value.ShareFile(r));

    // ───────────────────────────── Docs ─────────────────────────────

    [Display(Name = GoogleWorkspaceMethods.DocsCreate)]
    [Description("Create a new Google Doc with an optional initial body of text.")]
    [Parameters("""{"type":"object","properties":{"title":{"type":"string","description":"Document title"},"text":{"type":"string","description":"Optional initial body text"}},"required":["title"]}""")]
    public Task<object> DocsCreate(ServiceConfig config, ServiceRequest r) => Guard(() => _docs.Value.Create(r));

    [Display(Name = GoogleWorkspaceMethods.DocsGetText)]
    [Description("Read the full plain-text body of a Google Doc.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The document ID"}},"required":["fileId"]}""")]
    public Task<object> DocsGetText(ServiceConfig config, ServiceRequest r) => Guard(() => _docs.Value.GetText(r));

    [Display(Name = GoogleWorkspaceMethods.DocsAppendText)]
    [Description("Append text to the end of a Google Doc.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The document ID"},"text":{"type":"string","description":"Text to append"}},"required":["fileId","text"]}""")]
    public Task<object> DocsAppendText(ServiceConfig config, ServiceRequest r) => Guard(() => _docs.Value.AppendText(r));

    [Display(Name = GoogleWorkspaceMethods.DocsReplaceText)]
    [Description("Find and replace all occurrences of text in a Google Doc.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The document ID"},"find":{"type":"string","description":"Text to find"},"replace":{"type":"string","description":"Replacement text"},"matchCase":{"type":"boolean","description":"Case-sensitive match (default false)"}},"required":["fileId","find"]}""")]
    public Task<object> DocsReplaceText(ServiceConfig config, ServiceRequest r) => Guard(() => _docs.Value.ReplaceText(r));

    // ───────────────────────────── Sheets ─────────────────────────────

    [Display(Name = GoogleWorkspaceMethods.SheetsCreate)]
    [Description("Create a new Google Sheets spreadsheet.")]
    [Parameters("""{"type":"object","properties":{"title":{"type":"string","description":"Spreadsheet title"}},"required":["title"]}""")]
    public Task<object> SheetsCreate(ServiceConfig config, ServiceRequest r) => Guard(() => _sheets.Value.Create(r));

    [Display(Name = GoogleWorkspaceMethods.SheetsGetValues)]
    [Description("Read cell values from an A1 range of a spreadsheet.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The spreadsheet ID"},"range":{"type":"string","description":"A1 notation range, e.g. 'Sheet1!A1:C10'"}},"required":["fileId","range"]}""")]
    public Task<object> SheetsGetValues(ServiceConfig config, ServiceRequest r) => Guard(() => _sheets.Value.GetValues(r));

    [Display(Name = GoogleWorkspaceMethods.SheetsUpdateValues)]
    [Description("Write a 2D array of values to an A1 range of a spreadsheet.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The spreadsheet ID"},"range":{"type":"string","description":"A1 notation range to write to"},"values":{"type":"array","description":"2D array of rows; each row is an array of cell values","items":{"type":"array","items":{}}}},"required":["fileId","range","values"]}""")]
    public Task<object> SheetsUpdateValues(ServiceConfig config, ServiceRequest r) => Guard(() => _sheets.Value.UpdateValues(r));

    [Display(Name = GoogleWorkspaceMethods.SheetsAppendRow)]
    [Description("Append one or more rows to a spreadsheet after the last row of data.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The spreadsheet ID"},"range":{"type":"string","description":"Range/table to append into, e.g. 'Sheet1!A1' (default A1)"},"values":{"type":"array","description":"2D array of rows to append","items":{"type":"array","items":{}}}},"required":["fileId","values"]}""")]
    public Task<object> SheetsAppendRow(ServiceConfig config, ServiceRequest r) => Guard(() => _sheets.Value.AppendRow(r));

    [Display(Name = GoogleWorkspaceMethods.SheetsClearValues)]
    [Description("Clear all values from an A1 range of a spreadsheet.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The spreadsheet ID"},"range":{"type":"string","description":"A1 notation range to clear"}},"required":["fileId","range"]}""")]
    public Task<object> SheetsClearValues(ServiceConfig config, ServiceRequest r) => Guard(() => _sheets.Value.ClearValues(r));

    [Display(Name = GoogleWorkspaceMethods.SheetsListSheets)]
    [Description("List the tabs (sheets) in a spreadsheet with their titles, IDs and grid sizes.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The spreadsheet ID"}},"required":["fileId"]}""")]
    public Task<object> SheetsListSheets(ServiceConfig config, ServiceRequest r) => Guard(() => _sheets.Value.ListSheets(r));

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Google Workspace operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
