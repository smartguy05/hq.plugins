using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.WebSearch.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Base URL of the web search API, e.g. https://api.search.brave.com/res/v1")]
    public string WebSearchUrl { get; set; }

    [Tooltip("API key for the web search service")]
    public string WebSearchApiKey { get; set; }
}
