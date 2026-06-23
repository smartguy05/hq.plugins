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
    [Parameters(typeof(DriveListFilesArgs))]
    public Task<object> DriveListFiles(ServiceConfig config, DriveListFilesArgs request) => Guard(() => _drive.Value.ListFiles(request));

    [Display(Name = GoogleWorkspaceMethods.DriveSearchFiles)]
    [Description("Full-text search across the user's Google Drive and return matching files.")]
    [Parameters(typeof(DriveSearchFilesArgs))]
    public Task<object> DriveSearchFiles(ServiceConfig config, DriveSearchFilesArgs request) => Guard(() => _drive.Value.SearchFiles(request));

    [Display(Name = GoogleWorkspaceMethods.DriveGetFile)]
    [Description("Get metadata for a single Drive file (name, type, size, link, parents).")]
    [Parameters(typeof(DriveGetFileArgs))]
    public Task<object> DriveGetFile(ServiceConfig config, DriveGetFileArgs request) => Guard(() => _drive.Value.GetFile(request));

    [Display(Name = GoogleWorkspaceMethods.DriveDownloadFile)]
    [Description("Download a Drive file's contents as base64. Google-native files (Docs/Sheets/Slides) are exported; override the export format with mimeType.")]
    [Parameters(typeof(DriveDownloadFileArgs))]
    public Task<object> DriveDownloadFile(ServiceConfig config, DriveDownloadFileArgs request) => Guard(() => _drive.Value.DownloadFile(request));

    [Display(Name = GoogleWorkspaceMethods.DriveUploadFile)]
    [Description("Upload a new file to Drive from base64-encoded content.")]
    [Parameters(typeof(DriveUploadFileArgs))]
    public Task<object> DriveUploadFile(ServiceConfig config, DriveUploadFileArgs request) => Guard(() => _drive.Value.UploadFile(request));

    [Display(Name = GoogleWorkspaceMethods.DriveCreateFolder)]
    [Description("Create a new folder in Drive.")]
    [Parameters(typeof(DriveCreateFolderArgs))]
    public Task<object> DriveCreateFolder(ServiceConfig config, DriveCreateFolderArgs request) => Guard(() => _drive.Value.CreateFolder(request));

    [Display(Name = GoogleWorkspaceMethods.DriveMoveFile)]
    [Description("Move a file to a different folder.")]
    [Parameters(typeof(DriveMoveFileArgs))]
    public Task<object> DriveMoveFile(ServiceConfig config, DriveMoveFileArgs request) => Guard(() => _drive.Value.MoveFile(request));

    [Display(Name = GoogleWorkspaceMethods.DriveCopyFile)]
    [Description("Create a copy of a Drive file, optionally with a new name and destination folder.")]
    [Parameters(typeof(DriveCopyFileArgs))]
    public Task<object> DriveCopyFile(ServiceConfig config, DriveCopyFileArgs request) => Guard(() => _drive.Value.CopyFile(request));

    [Display(Name = GoogleWorkspaceMethods.DriveDeleteFile)]
    [Description("Delete a Drive file. Moves it to trash by default; set permanent=true to delete forever.")]
    [Parameters(typeof(DriveDeleteFileArgs))]
    public Task<object> DriveDeleteFile(ServiceConfig config, DriveDeleteFileArgs request) => Guard(() => _drive.Value.DeleteFile(request));

    [Display(Name = GoogleWorkspaceMethods.DriveShareFile)]
    [Description("Share a Drive file by creating a permission and returning a shareable link.")]
    [Parameters(typeof(DriveShareFileArgs))]
    public Task<object> DriveShareFile(ServiceConfig config, DriveShareFileArgs request) => Guard(() => _drive.Value.ShareFile(request));

    // ───────────────────────────── Docs ─────────────────────────────

    [Display(Name = GoogleWorkspaceMethods.DocsCreate)]
    [Description("Create a new Google Doc with an optional initial body of text.")]
    [Parameters(typeof(DocsCreateArgs))]
    public Task<object> DocsCreate(ServiceConfig config, DocsCreateArgs request) => Guard(() => _docs.Value.Create(request));

    [Display(Name = GoogleWorkspaceMethods.DocsGetText)]
    [Description("Read the full plain-text body of a Google Doc.")]
    [Parameters(typeof(DocsGetTextArgs))]
    public Task<object> DocsGetText(ServiceConfig config, DocsGetTextArgs request) => Guard(() => _docs.Value.GetText(request));

    [Display(Name = GoogleWorkspaceMethods.DocsAppendText)]
    [Description("Append text to the end of a Google Doc.")]
    [Parameters(typeof(DocsAppendTextArgs))]
    public Task<object> DocsAppendText(ServiceConfig config, DocsAppendTextArgs request) => Guard(() => _docs.Value.AppendText(request));

    [Display(Name = GoogleWorkspaceMethods.DocsReplaceText)]
    [Description("Find and replace all occurrences of text in a Google Doc.")]
    [Parameters(typeof(DocsReplaceTextArgs))]
    public Task<object> DocsReplaceText(ServiceConfig config, DocsReplaceTextArgs request) => Guard(() => _docs.Value.ReplaceText(request));

    // ───────────────────────────── Sheets ─────────────────────────────

    [Display(Name = GoogleWorkspaceMethods.SheetsCreate)]
    [Description("Create a new Google Sheets spreadsheet.")]
    [Parameters(typeof(SheetsCreateArgs))]
    public Task<object> SheetsCreate(ServiceConfig config, SheetsCreateArgs request) => Guard(() => _sheets.Value.Create(request));

    [Display(Name = GoogleWorkspaceMethods.SheetsGetValues)]
    [Description("Read cell values from an A1 range of a spreadsheet.")]
    [Parameters(typeof(SheetsGetValuesArgs))]
    public Task<object> SheetsGetValues(ServiceConfig config, SheetsGetValuesArgs request) => Guard(() => _sheets.Value.GetValues(request));

    [Display(Name = GoogleWorkspaceMethods.SheetsUpdateValues)]
    [Description("Write a 2D array of values to an A1 range of a spreadsheet.")]
    [Parameters(typeof(SheetsUpdateValuesArgs))]
    public Task<object> SheetsUpdateValues(ServiceConfig config, SheetsUpdateValuesArgs request) => Guard(() => _sheets.Value.UpdateValues(request));

    [Display(Name = GoogleWorkspaceMethods.SheetsAppendRow)]
    [Description("Append one or more rows to a spreadsheet after the last row of data.")]
    [Parameters(typeof(SheetsAppendRowArgs))]
    public Task<object> SheetsAppendRow(ServiceConfig config, SheetsAppendRowArgs request) => Guard(() => _sheets.Value.AppendRow(request));

    [Display(Name = GoogleWorkspaceMethods.SheetsClearValues)]
    [Description("Clear all values from an A1 range of a spreadsheet.")]
    [Parameters(typeof(SheetsClearValuesArgs))]
    public Task<object> SheetsClearValues(ServiceConfig config, SheetsClearValuesArgs request) => Guard(() => _sheets.Value.ClearValues(request));

    [Display(Name = GoogleWorkspaceMethods.SheetsListSheets)]
    [Description("List the tabs (sheets) in a spreadsheet with their titles, IDs and grid sizes.")]
    [Parameters(typeof(SheetsListSheetsArgs))]
    public Task<object> SheetsListSheets(ServiceConfig config, SheetsListSheetsArgs request) => Guard(() => _sheets.Value.ListSheets(request));

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
