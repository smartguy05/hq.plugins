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
    [Parameters(typeof(FilesListArgs))]
    public Task<object> FilesList(ServiceConfig config, FilesListArgs request) => Guard(() => _files.Value.List(request));

    [Display(Name = Microsoft365Methods.FilesSearch)]
    [Description("Search a drive for files and folders matching a query.")]
    [Parameters(typeof(FilesSearchArgs))]
    public Task<object> FilesSearch(ServiceConfig config, FilesSearchArgs request) => Guard(() => _files.Value.Search(request));

    [Display(Name = Microsoft365Methods.FilesGet)]
    [Description("Get metadata for a single drive item by id or path.")]
    [Parameters(typeof(FilesGetArgs))]
    public Task<object> FilesGet(ServiceConfig config, FilesGetArgs request) => Guard(() => _files.Value.Get(request));

    [Display(Name = Microsoft365Methods.FilesDownload)]
    [Description("Download a drive item's contents as base64.")]
    [Parameters(typeof(FilesDownloadArgs))]
    public Task<object> FilesDownload(ServiceConfig config, FilesDownloadArgs request) => Guard(() => _files.Value.Download(request));

    [Display(Name = Microsoft365Methods.FilesUpload)]
    [Description("Upload a new file from base64 content into a folder (itemId/path = destination folder, default root).")]
    [Parameters(typeof(FilesUploadArgs))]
    public Task<object> FilesUpload(ServiceConfig config, FilesUploadArgs request) => Guard(() => _files.Value.Upload(request));

    [Display(Name = Microsoft365Methods.FilesCreateFolder)]
    [Description("Create a new folder in a drive.")]
    [Parameters(typeof(FilesCreateFolderArgs))]
    public Task<object> FilesCreateFolder(ServiceConfig config, FilesCreateFolderArgs request) => Guard(() => _files.Value.CreateFolder(request));

    [Display(Name = Microsoft365Methods.FilesMove)]
    [Description("Move (and optionally rename) a drive item to another folder.")]
    [Parameters(typeof(FilesMoveArgs))]
    public Task<object> FilesMove(ServiceConfig config, FilesMoveArgs request) => Guard(() => _files.Value.Move(request));

    [Display(Name = Microsoft365Methods.FilesCopy)]
    [Description("Copy a drive item, optionally to another folder and/or with a new name.")]
    [Parameters(typeof(FilesCopyArgs))]
    public Task<object> FilesCopy(ServiceConfig config, FilesCopyArgs request) => Guard(() => _files.Value.Copy(request));

    [Display(Name = Microsoft365Methods.FilesDelete)]
    [Description("Delete a drive item (moves it to the recycle bin).")]
    [Parameters(typeof(FilesDeleteArgs))]
    public Task<object> FilesDelete(ServiceConfig config, FilesDeleteArgs request) => Guard(() => _files.Value.Delete(request));

    [Display(Name = Microsoft365Methods.FilesShare)]
    [Description("Create a sharing link for a drive item and return its URL.")]
    [Parameters(typeof(FilesShareArgs))]
    public Task<object> FilesShare(ServiceConfig config, FilesShareArgs request) => Guard(() => _files.Value.Share(request));

    // ───────────────────────────── Excel ─────────────────────────────

    [Display(Name = Microsoft365Methods.ExcelListWorksheets)]
    [Description("List the worksheets (tabs) in an Excel workbook stored in a drive.")]
    [Parameters(typeof(ExcelListWorksheetsArgs))]
    public Task<object> ExcelListWorksheets(ServiceConfig config, ExcelListWorksheetsArgs request) => Guard(() => _excel.Value.ListWorksheets(request));

    [Display(Name = Microsoft365Methods.ExcelGetRange)]
    [Description("Read cell values from an A1 range of an Excel worksheet.")]
    [Parameters(typeof(ExcelGetRangeArgs))]
    public Task<object> ExcelGetRange(ServiceConfig config, ExcelGetRangeArgs request) => Guard(() => _excel.Value.GetRange(request));

    [Display(Name = Microsoft365Methods.ExcelUpdateRange)]
    [Description("Write a 2D array of values to an A1 range of an Excel worksheet.")]
    [Parameters(typeof(ExcelUpdateRangeArgs))]
    public Task<object> ExcelUpdateRange(ServiceConfig config, ExcelUpdateRangeArgs request) => Guard(() => _excel.Value.UpdateRange(request));

    [Display(Name = Microsoft365Methods.ExcelAppendRow)]
    [Description("Append one or more rows below the used range of an Excel worksheet.")]
    [Parameters(typeof(ExcelAppendRowArgs))]
    public Task<object> ExcelAppendRow(ServiceConfig config, ExcelAppendRowArgs request) => Guard(() => _excel.Value.AppendRow(request));

    [Display(Name = Microsoft365Methods.ExcelAddWorksheet)]
    [Description("Add a new worksheet (tab) to an Excel workbook.")]
    [Parameters(typeof(ExcelAddWorksheetArgs))]
    public Task<object> ExcelAddWorksheet(ServiceConfig config, ExcelAddWorksheetArgs request) => Guard(() => _excel.Value.AddWorksheet(request));

    // ───────────────────────────── Word ─────────────────────────────

    [Display(Name = Microsoft365Methods.WordCreate)]
    [Description("Create a new Word (.docx) document from plain text and save it to a drive.")]
    [Parameters(typeof(WordCreateArgs))]
    public Task<object> WordCreate(ServiceConfig config, WordCreateArgs request) => Guard(() => _word.Value.Create(request));

    [Display(Name = Microsoft365Methods.WordRead)]
    [Description("Read the plain text of a Word (.docx) document stored in a drive.")]
    [Parameters(typeof(WordReadArgs))]
    public Task<object> WordRead(ServiceConfig config, WordReadArgs request) => Guard(() => _word.Value.Read(request));

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
