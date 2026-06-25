using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;

namespace HQ.Plugins.Perplexity.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

public class PerplexitySearchArgs
{
    [Required, Description("The research question or search query.")]
    public string Query { get; set; }

    [Description("Optional. Restrict sources by age: 'day', 'week', 'month', or 'year'.")]
    public string Recency { get; set; }

    [Description("Optional. Domains to include, or exclude by prefixing with '-' (e.g. 'wikipedia.org', '-pinterest.com'). Merged with configured defaults.")]
    public List<string> DomainFilters { get; set; }

    [Description("Optional model override, e.g. 'sonar' or 'sonar-pro'. Defaults to the configured search model.")]
    public string Model { get; set; }
}

public class PerplexityDeepResearchArgs
{
    [Required, Description("The research question. Be specific — this runs an exhaustive multi-step investigation.")]
    public string Query { get; set; }

    [Description("Optional. Restrict sources by age: 'day', 'week', 'month', or 'year'.")]
    public string Recency { get; set; }

    [Description("Optional. Domains to include, or exclude by prefixing with '-'. Merged with configured defaults.")]
    public List<string> DomainFilters { get; set; }

    /// <summary>Injected by the host — identifies the conversation, used to deliver async results back.</summary>
    [Injected]
    public string ConversationId { get; set; }

    /// <summary>Injected by the host — the service that called the tool; used to route async results back.</summary>
    [Injected]
    public string RequestingService { get; set; }
}
