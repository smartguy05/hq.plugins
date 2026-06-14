namespace HQ.Plugins.Microsoft365;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on Microsoft365Service.</summary>
public static class Microsoft365Methods
{
    // OneDrive / SharePoint files
    public const string FilesList = "files_list";
    public const string FilesSearch = "files_search";
    public const string FilesGet = "files_get";
    public const string FilesDownload = "files_download";
    public const string FilesUpload = "files_upload";
    public const string FilesCreateFolder = "files_create_folder";
    public const string FilesMove = "files_move";
    public const string FilesCopy = "files_copy";
    public const string FilesDelete = "files_delete";
    public const string FilesShare = "files_share";

    // Excel
    public const string ExcelListWorksheets = "excel_list_worksheets";
    public const string ExcelGetRange = "excel_get_range";
    public const string ExcelUpdateRange = "excel_update_range";
    public const string ExcelAppendRow = "excel_append_row";
    public const string ExcelAddWorksheet = "excel_add_worksheet";

    // Word
    public const string WordCreate = "word_create";
    public const string WordRead = "word_read";
}
