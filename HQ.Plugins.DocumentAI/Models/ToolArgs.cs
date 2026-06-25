using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.DocumentAI.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

public class ExtractTextArgs
{
    [Description("Base64-encoded image/PDF bytes")]
    public string Content { get; set; }

    [Description("Public or GCS image URI (alternative to content)")]
    public string ImageUri { get; set; }
}

public class ExtractReceiptArgs
{
    [Required, Description("Base64-encoded receipt image/PDF bytes")]
    public string Content { get; set; }

    [Description("e.g. image/jpeg, image/png, application/pdf")]
    public string MimeType { get; set; }
}

public class ExtractDocumentFieldsArgs
{
    [Required, Description("Base64-encoded document bytes")]
    public string Content { get; set; }

    [Description("e.g. application/pdf, image/jpeg")]
    public string MimeType { get; set; }
}
