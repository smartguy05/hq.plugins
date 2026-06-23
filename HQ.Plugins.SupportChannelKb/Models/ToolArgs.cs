using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;

namespace HQ.Plugins.SupportChannelKb.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class SearchSupportChannelsArgs
{
    [Required, Description("The search text to find relevant knowledge base entries")]
    public string SearchCriteria { get; set; }
}

public class AddSupportChannelCollectionArgs
{
    [Required, Description("The name of the new collection to create")]
    public string SupportChannel { get; set; }

    [Description("A description of the collection")]
    public string Description { get; set; }
}

public class SaveSupportChannelInformationArgs
{
    [Required, Description("The new information text to save to the collection")]
    public string NewInformation { get; set; }

    [Description("A description or context for the information")]
    public string Description { get; set; }

    [Description("Optional metadata key-value pairs for the information")]
    public List<Dictionary<string, string>> NewInformationMetaData { get; set; } = new();
}
