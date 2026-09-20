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

    // WP6B-15: this type intentionally has NO ConversationId/RequestingService properties.
    // [Injected] only hides a field from the LLM-visible schema (ToolSchemaGenerator) — it does
    // NOT stop DeserializeArgs from binding a same-named key a model puts in its tool-call JSON
    // (case-insensitively, regardless of the schema). A previous version bound ConversationId
    // this way to deliver deep-research results back into the conversation asynchronously; a
    // prompt-injected model could emit its own "conversationId" and redirect delivery to an
    // arbitrary (possibly cross-tenant) conversation, on a background thread with no tenant
    // context. There is no host-trusted per-call conversation id available to a plugin today
    // (unlike organizationId, which PluginService force-injects before Execute — see
    // PluginService.InjectOrganizationId) — until a matching host-side force-injection +
    // tenant-scoped delivery lands, this plugin does not offer conversation-targeted delivery at
    // all: PerplexityDeepResearch always runs synchronously and returns the answer directly.
}
