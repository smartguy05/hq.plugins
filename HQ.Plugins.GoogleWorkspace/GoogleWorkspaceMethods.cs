namespace HQ.Plugins.GoogleWorkspace;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on GoogleWorkspaceService.</summary>
public static class GoogleWorkspaceMethods
{
    // Drive
    public const string DriveListFiles = "drive_list_files";
    public const string DriveSearchFiles = "drive_search_files";
    public const string DriveGetFile = "drive_get_file";
    public const string DriveDownloadFile = "drive_download_file";
    public const string DriveUploadFile = "drive_upload_file";
    public const string DriveCreateFolder = "drive_create_folder";
    public const string DriveMoveFile = "drive_move_file";
    public const string DriveCopyFile = "drive_copy_file";
    public const string DriveDeleteFile = "drive_delete_file";
    public const string DriveShareFile = "drive_share_file";

    // Docs
    public const string DocsCreate = "docs_create";
    public const string DocsGetText = "docs_get_text";
    public const string DocsAppendText = "docs_append_text";
    public const string DocsReplaceText = "docs_replace_text";

    // Sheets
    public const string SheetsCreate = "sheets_create";
    public const string SheetsGetValues = "sheets_get_values";
    public const string SheetsUpdateValues = "sheets_update_values";
    public const string SheetsAppendRow = "sheets_append_row";
    public const string SheetsClearValues = "sheets_clear_values";
    public const string SheetsListSheets = "sheets_list_sheets";
}
