using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.UseMemos.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable). The confirmation tool implements
/// <see cref="IPluginServiceRequest"/> so the request survives the confirmation replay round-trip.
/// </summary>

public class ReadMemosArgs
{
    [Description("The type of data to read: 'memos' or 'resources'. Defaults to 'memos'.")]
    public string DataType { get; set; } = "memos";

    [Description("Optional UID to retrieve a specific memo")]
    public string Uid { get; set; }
}

public class AddMemoArgs : IPluginServiceRequest
{
    // Framework envelope fields — supplied by the orchestrator, hidden from the LLM schema,
    // preserved across the confirmation replay.
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The text content of the memo to create")]
    public string Content { get; set; }

    [Description("Visibility level: 'PUBLIC', 'PROTECTED', or 'PRIVATE'. Defaults to 'PRIVATE'.")]
    public string Visibility { get; set; }
}

public class UpdateMemoArgs
{
    [Required, Description("The UID of the memo to update")]
    public string Uid { get; set; }

    [Description("The new content for the memo")]
    public string Content { get; set; }

    [Description("Updated visibility level: 'PUBLIC', 'PROTECTED', or 'PRIVATE'")]
    public string Visibility { get; set; }
}
