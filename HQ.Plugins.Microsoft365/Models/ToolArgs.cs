using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Helpers;

namespace HQ.Plugins.Microsoft365.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

// ─────────────────────── OneDrive / SharePoint ───────────────────────

public class FilesListArgs
{
    [Description("Drive ID (omit to use the configured default)")]
    public string DriveId { get; set; }

    [Description("Folder item ID to list (omit for root)")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Folder path relative to root, e.g. 'Reports/2026'")]
    public string Path { get; set; }

    [Description("Max results (1-999, default 100)")]
    public int? PageSize { get; set; }
}

public class FilesSearchArgs
{
    [Description("Drive ID (omit to use the configured default)")]
    public string DriveId { get; set; }

    [Required, Description("Search text")]
    public string Query { get; set; }

    [Description("Max results (1-999, default 50)")]
    public int? PageSize { get; set; }
}

public class FilesGetArgs
{
    public string DriveId { get; set; }

    [Description("The drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Item path relative to root")]
    public string Path { get; set; }
}

public class FilesDownloadArgs
{
    public string DriveId { get; set; }

    [Description("The drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Item path relative to root")]
    public string Path { get; set; }
}

public class FilesUploadArgs
{
    public string DriveId { get; set; }

    [Description("Destination folder item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Destination folder path")]
    public string Path { get; set; }

    [Required, Description("File name")]
    public string Name { get; set; }

    [Required, Description("Base64-encoded file bytes")]
    public string Content { get; set; }
}

public class FilesCreateFolderArgs
{
    public string DriveId { get; set; }

    [Description("Parent folder item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Parent folder path")]
    public string Path { get; set; }

    [Required, Description("New folder name")]
    public string Name { get; set; }
}

public class FilesMoveArgs
{
    public string DriveId { get; set; }

    [Required, Description("The item to move")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Required, Description("Target folder item ID")]
    public string DestinationFolderId { get; set; }

    [Description("Optional new name")]
    public string Name { get; set; }
}

public class FilesCopyArgs
{
    public string DriveId { get; set; }

    [Required, Description("The item to copy")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Target folder item ID")]
    public string DestinationFolderId { get; set; }

    [Description("Optional name for the copy")]
    public string Name { get; set; }
}

public class FilesDeleteArgs
{
    public string DriveId { get; set; }

    [Required, Description("The item to delete")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }
}

public class FilesShareArgs
{
    public string DriveId { get; set; }

    [Required, Description("The item to share")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("view | edit (default view)")]
    public string LinkType { get; set; }

    [Description("anonymous | organization (default anonymous)")]
    public string Scope { get; set; }
}

// ───────────────────────────── Excel ─────────────────────────────

public class ExcelListWorksheetsArgs
{
    public string DriveId { get; set; }

    [Required, Description("The .xlsx drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }
}

public class ExcelGetRangeArgs
{
    public string DriveId { get; set; }

    [Required, Description("The .xlsx drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Required, Description("Worksheet name")]
    public string Worksheet { get; set; }

    [Required, Description("A1 address, e.g. 'A1:C10'")]
    public string Range { get; set; }

    /// <summary>Alternate worksheet field consulted by the body; not advertised.</summary>
    [Injected]
    public string WorksheetName { get; set; }
}

public class ExcelUpdateRangeArgs
{
    public string DriveId { get; set; }

    [Required, Description("The .xlsx drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Required, Description("Worksheet name")]
    public string Worksheet { get; set; }

    [Required, Description("A1 address to write to")]
    public string Range { get; set; }

    [Required, Description("2D array of rows")]
    public List<List<JsonElement>> Values { get; set; }

    [Injected]
    public string WorksheetName { get; set; }
}

public class ExcelAppendRowArgs
{
    public string DriveId { get; set; }

    [Required, Description("The .xlsx drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Required, Description("Worksheet name")]
    public string Worksheet { get; set; }

    [Required, Description("2D array of rows to append")]
    public List<List<JsonElement>> Values { get; set; }

    [Injected]
    public string WorksheetName { get; set; }
}

public class ExcelAddWorksheetArgs
{
    public string DriveId { get; set; }

    [Required, Description("The .xlsx drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Required, Description("New worksheet name")]
    public string Name { get; set; }

    /// <summary>Alternate name field consulted by the body; not advertised.</summary>
    [Injected]
    public string WorksheetName { get; set; }
}

// ───────────────────────────── Word ─────────────────────────────

public class WordCreateArgs
{
    public string DriveId { get; set; }

    [Description("Destination folder item ID (default root)")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Required, Description("File name (.docx appended if missing)")]
    public string Name { get; set; }

    [Description("Document body text; newlines become paragraphs")]
    public string Text { get; set; }
}

public class WordReadArgs
{
    public string DriveId { get; set; }

    [Description("The .docx drive item ID")]
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ItemId { get; set; }

    [Description("Document path relative to root")]
    public string Path { get; set; }
}
