using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.WebReader.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. None of the WebReader tools read framework envelope fields or use a confirmation flow,
/// so no <c>[Injected]</c> properties are required here.
/// </summary>

public class ReadPageArgs
{
    [Required, Description("The URL of the page to read")]
    public string Url { get; set; }

    [Description("Optional cap on the number of markdown characters returned. Defaults to the configured maximum.")]
    public int? MaxLength { get; set; }
}

public class ExtractLinksArgs
{
    [Required, Description("The URL of the page to extract links from")]
    public string Url { get; set; }

    [Description("Optional substring; only links whose text or URL contains it are returned (case-insensitive).")]
    public string Filter { get; set; }
}

public class SearchPageArgs
{
    [Required, Description("The URL of the page to search")]
    public string Url { get; set; }

    [Required, Description("Text to find within the page (case-insensitive)")]
    public string Query { get; set; }

    [Description("Characters of context to include around each match. Defaults to 200.")]
    public int? ContextChars { get; set; }
}
