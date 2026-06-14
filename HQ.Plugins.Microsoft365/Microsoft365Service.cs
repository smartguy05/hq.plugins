using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Microsoft365.Graph;
using HQ.Plugins.Microsoft365.Models;

namespace HQ.Plugins.Microsoft365;

/// <summary>
/// Tool surface for Microsoft 365: OneDrive/SharePoint files, Excel workbooks, Word documents.
/// Annotations are scanned by ServiceExtensions.GetServiceToolCalls; ProcessRequest dispatches
/// by matching [Display(Name)] to ServiceRequest.Method.
/// </summary>
public class Microsoft365Service
{
    private readonly LogDelegate _logger;
    private readonly Lazy<FilesClient> _files;
    private readonly Lazy<ExcelClient> _excel;
    private readonly Lazy<WordClient> _word;

    public Microsoft365Service(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _files = new Lazy<FilesClient>(() => new FilesClient(config));
        _excel = new Lazy<ExcelClient>(() => new ExcelClient(config));
        _word = new Lazy<WordClient>(() => new WordClient(config));
    }

    // ─────────────────────── OneDrive / SharePoint ───────────────────────

    [Display(Name = Microsoft365Methods.FilesList)]
    [Description("List files and folders in a OneDrive/SharePoint drive folder.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string","description":"Drive ID (omit to use the configured default)"},"itemId":{"type":"string","description":"Folder item ID to list (omit for root)"},"path":{"type":"string","description":"Folder path relative to root, e.g. 'Reports/2026'"},"pageSize":{"type":"integer","description":"Max results (1-999, default 100)"}},"required":[]}""")]
    public Task<object> FilesList(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.List(r));

    [Display(Name = Microsoft365Methods.FilesSearch)]
    [Description("Search a drive for files and folders matching a query.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string","description":"Drive ID (omit to use the configured default)"},"query":{"type":"string","description":"Search text"},"pageSize":{"type":"integer","description":"Max results (1-999, default 50)"}},"required":["query"]}""")]
    public Task<object> FilesSearch(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Search(r));

    [Display(Name = Microsoft365Methods.FilesGet)]
    [Description("Get metadata for a single drive item by id or path.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The drive item ID"},"path":{"type":"string","description":"Item path relative to root"}},"required":[]}""")]
    public Task<object> FilesGet(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Get(r));

    [Display(Name = Microsoft365Methods.FilesDownload)]
    [Description("Download a drive item's contents as base64.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The drive item ID"},"path":{"type":"string","description":"Item path relative to root"}},"required":[]}""")]
    public Task<object> FilesDownload(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Download(r));

    [Display(Name = Microsoft365Methods.FilesUpload)]
    [Description("Upload a new file from base64 content into a folder (itemId/path = destination folder, default root).")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"Destination folder item ID"},"path":{"type":"string","description":"Destination folder path"},"name":{"type":"string","description":"File name"},"content":{"type":"string","description":"Base64-encoded file bytes"}},"required":["name","content"]}""")]
    public Task<object> FilesUpload(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Upload(r));

    [Display(Name = Microsoft365Methods.FilesCreateFolder)]
    [Description("Create a new folder in a drive.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"Parent folder item ID"},"path":{"type":"string","description":"Parent folder path"},"name":{"type":"string","description":"New folder name"}},"required":["name"]}""")]
    public Task<object> FilesCreateFolder(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.CreateFolder(r));

    [Display(Name = Microsoft365Methods.FilesMove)]
    [Description("Move (and optionally rename) a drive item to another folder.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The item to move"},"destinationFolderId":{"type":"string","description":"Target folder item ID"},"name":{"type":"string","description":"Optional new name"}},"required":["itemId","destinationFolderId"]}""")]
    public Task<object> FilesMove(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Move(r));

    [Display(Name = Microsoft365Methods.FilesCopy)]
    [Description("Copy a drive item, optionally to another folder and/or with a new name.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The item to copy"},"destinationFolderId":{"type":"string","description":"Target folder item ID"},"name":{"type":"string","description":"Optional name for the copy"}},"required":["itemId"]}""")]
    public Task<object> FilesCopy(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Copy(r));

    [Display(Name = Microsoft365Methods.FilesDelete)]
    [Description("Delete a drive item (moves it to the recycle bin).")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The item to delete"}},"required":["itemId"]}""")]
    public Task<object> FilesDelete(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Delete(r));

    [Display(Name = Microsoft365Methods.FilesShare)]
    [Description("Create a sharing link for a drive item and return its URL.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The item to share"},"linkType":{"type":"string","description":"view | edit (default view)"},"scope":{"type":"string","description":"anonymous | organization (default anonymous)"}},"required":["itemId"]}""")]
    public Task<object> FilesShare(ServiceConfig config, ServiceRequest r) => Guard(() => _files.Value.Share(r));

    // ───────────────────────────── Excel ─────────────────────────────

    [Display(Name = Microsoft365Methods.ExcelListWorksheets)]
    [Description("List the worksheets (tabs) in an Excel workbook stored in a drive.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The .xlsx drive item ID"}},"required":["itemId"]}""")]
    public Task<object> ExcelListWorksheets(ServiceConfig config, ServiceRequest r) => Guard(() => _excel.Value.ListWorksheets(r));

    [Display(Name = Microsoft365Methods.ExcelGetRange)]
    [Description("Read cell values from an A1 range of an Excel worksheet.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The .xlsx drive item ID"},"worksheet":{"type":"string","description":"Worksheet name"},"range":{"type":"string","description":"A1 address, e.g. 'A1:C10'"}},"required":["itemId","worksheet","range"]}""")]
    public Task<object> ExcelGetRange(ServiceConfig config, ServiceRequest r) => Guard(() => _excel.Value.GetRange(r));

    [Display(Name = Microsoft365Methods.ExcelUpdateRange)]
    [Description("Write a 2D array of values to an A1 range of an Excel worksheet.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The .xlsx drive item ID"},"worksheet":{"type":"string","description":"Worksheet name"},"range":{"type":"string","description":"A1 address to write to"},"values":{"type":"array","description":"2D array of rows","items":{"type":"array","items":{}}}},"required":["itemId","worksheet","range","values"]}""")]
    public Task<object> ExcelUpdateRange(ServiceConfig config, ServiceRequest r) => Guard(() => _excel.Value.UpdateRange(r));

    [Display(Name = Microsoft365Methods.ExcelAppendRow)]
    [Description("Append one or more rows below the used range of an Excel worksheet.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The .xlsx drive item ID"},"worksheet":{"type":"string","description":"Worksheet name"},"values":{"type":"array","description":"2D array of rows to append","items":{"type":"array","items":{}}}},"required":["itemId","worksheet","values"]}""")]
    public Task<object> ExcelAppendRow(ServiceConfig config, ServiceRequest r) => Guard(() => _excel.Value.AppendRow(r));

    [Display(Name = Microsoft365Methods.ExcelAddWorksheet)]
    [Description("Add a new worksheet (tab) to an Excel workbook.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The .xlsx drive item ID"},"name":{"type":"string","description":"New worksheet name"}},"required":["itemId","name"]}""")]
    public Task<object> ExcelAddWorksheet(ServiceConfig config, ServiceRequest r) => Guard(() => _excel.Value.AddWorksheet(r));

    // ───────────────────────────── Word ─────────────────────────────

    [Display(Name = Microsoft365Methods.WordCreate)]
    [Description("Create a new Word (.docx) document from plain text and save it to a drive.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"Destination folder item ID (default root)"},"name":{"type":"string","description":"File name (.docx appended if missing)"},"text":{"type":"string","description":"Document body text; newlines become paragraphs"}},"required":["name"]}""")]
    public Task<object> WordCreate(ServiceConfig config, ServiceRequest r) => Guard(() => _word.Value.Create(r));

    [Display(Name = Microsoft365Methods.WordRead)]
    [Description("Read the plain text of a Word (.docx) document stored in a drive.")]
    [Parameters("""{"type":"object","properties":{"driveId":{"type":"string"},"itemId":{"type":"string","description":"The .docx drive item ID"},"path":{"type":"string","description":"Document path relative to root"}},"required":[]}""")]
    public Task<object> WordRead(ServiceConfig config, ServiceRequest r) => Guard(() => _word.Value.Read(r));

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Microsoft 365 operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
