using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Perplexity.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// Injected by the host during initialization — identifies the agent that owns this plugin config.
    /// Used to route deep-research results back into the conversation.
    /// </summary>
    public Guid? AgentId { get; set; }

    [Tooltip("Perplexity API key (starts with pplx-). Found in your Perplexity account settings.")]
    public string PerplexityApiKey { get; set; }

    [Tooltip("Default model for perplexity_search, e.g. sonar-pro or sonar. Overridable per call.")]
    public string DefaultSearchModel { get; set; } = "sonar-pro";

    [Tooltip("Default domain filters applied to every search, e.g. [\"wikipedia.org\", \"-pinterest.com\"]. Merged with per-call filters.")]
    public List<string> DefaultDomainFilters { get; set; } = new();

    [Tooltip("Optional max_tokens ceiling sent to the API to bound cost, especially for deep research.")]
    public int? MaxTokens { get; set; }

    [Tooltip("Optional: name of the AI plugin to route async deep-research results through. Defaults to the service that called the tool.")]
    public string AiPlugin { get; set; }
}
