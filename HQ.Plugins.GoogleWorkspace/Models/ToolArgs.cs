using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Helpers;

namespace HQ.Plugins.GoogleWorkspace.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable). <see cref="StringOrNumberConverter"/> is applied to
/// id fields because LLMs frequently send numeric ids without quotes.
/// </summary>

// ───────────────────────────── Drive ─────────────────────────────

public class DriveListFilesArgs
{
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Folder ID to list children of (omit for Drive root)")]
    public string FolderId { get; set; }

    [Description("Optional raw Drive query clause, e.g. \"mimeType='application/pdf'\"")]
    public string Query { get; set; }

    [Description("Max results (1-1000, default 100)")]
    public int? PageSize { get; set; }

    [Description("Sort order, e.g. 'modifiedTime desc'")]
    public string OrderBy { get; set; }
}

public class DriveSearchFilesArgs
{
    [Required, Description("Text to search for in file names and contents")]
    public string Query { get; set; }

    [Description("Max results (1-1000, default 50)")]
    public int? PageSize { get; set; }
}

public class DriveGetFileArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The Drive file ID")]
    public string FileId { get; set; }
}

public class DriveDownloadFileArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The Drive file ID")]
    public string FileId { get; set; }

    [Description("Export MIME type for Google-native files, e.g. 'application/pdf'")]
    public string MimeType { get; set; }
}

public class DriveUploadFileArgs
{
    [Required, Description("File name")]
    public string Name { get; set; }

    [Required, Description("Base64-encoded file bytes")]
    public string Content { get; set; }

    [Description("MIME type of the file")]
    public string MimeType { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Destination folder ID (omit for root)")]
    public string FolderId { get; set; }
}

public class DriveCreateFolderArgs
{
    [Required, Description("Folder name")]
    public string Name { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Parent folder ID (omit for root)")]
    public string FolderId { get; set; }
}

public class DriveMoveFileArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The file to move")]
    public string FileId { get; set; }

    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Destination folder ID")]
    public string FolderId { get; set; }
}

public class DriveCopyFileArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The file to copy")]
    public string FileId { get; set; }

    [Description("Name for the copy")]
    public string Name { get; set; }

    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("Destination folder ID")]
    public string FolderId { get; set; }
}

public class DriveDeleteFileArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The file to delete")]
    public string FileId { get; set; }

    [Description("Permanently delete instead of trashing")]
    public bool? Permanent { get; set; }
}

public class DriveShareFileArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The file to share")]
    public string FileId { get; set; }

    [Description("reader | writer | commenter | owner (default reader)")]
    public string Role { get; set; }

    [Description("user | group | domain | anyone (default anyone)")]
    public string Type { get; set; }

    [Description("Email for user/group grants")]
    public string EmailAddress { get; set; }
}

// ───────────────────────────── Docs ─────────────────────────────

public class DocsCreateArgs
{
    [Required, Description("Document title")]
    public string Title { get; set; }

    [Description("Optional initial body text")]
    public string Text { get; set; }

    /// <summary>Not advertised; legacy fallback when Title is absent.</summary>
    [Injected] public string Name { get; set; }
}

public class DocsGetTextArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The document ID")]
    public string FileId { get; set; }
}

public class DocsAppendTextArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The document ID")]
    public string FileId { get; set; }

    [Required, Description("Text to append")]
    public string Text { get; set; }
}

public class DocsReplaceTextArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The document ID")]
    public string FileId { get; set; }

    [Required, Description("Text to find")]
    public string Find { get; set; }

    [Description("Replacement text")]
    public string Replace { get; set; }

    [Description("Case-sensitive match (default false)")]
    public bool? MatchCase { get; set; }
}

// ───────────────────────────── Sheets ─────────────────────────────

public class SheetsCreateArgs
{
    [Required, Description("Spreadsheet title")]
    public string Title { get; set; }

    /// <summary>Not advertised; legacy fallback when Title is absent.</summary>
    [Injected] public string Name { get; set; }
}

public class SheetsGetValuesArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The spreadsheet ID")]
    public string FileId { get; set; }

    [Required, Description("A1 notation range, e.g. 'Sheet1!A1:C10'")]
    public string Range { get; set; }
}

public class SheetsUpdateValuesArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The spreadsheet ID")]
    public string FileId { get; set; }

    [Required, Description("A1 notation range to write to")]
    public string Range { get; set; }

    [Required, Description("2D array of rows; each row is an array of cell values")]
    public List<List<JsonElement>> Values { get; set; }
}

public class SheetsAppendRowArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The spreadsheet ID")]
    public string FileId { get; set; }

    [Description("Range/table to append into, e.g. 'Sheet1!A1' (default A1)")]
    public string Range { get; set; }

    [Required, Description("2D array of rows to append")]
    public List<List<JsonElement>> Values { get; set; }
}

public class SheetsClearValuesArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The spreadsheet ID")]
    public string FileId { get; set; }

    [Required, Description("A1 notation range to clear")]
    public string Range { get; set; }
}

public class SheetsListSheetsArgs
{
    [Required]
    [JsonConverter(typeof(StringOrNumberConverter))]
    [Description("The spreadsheet ID")]
    public string FileId { get; set; }
}
