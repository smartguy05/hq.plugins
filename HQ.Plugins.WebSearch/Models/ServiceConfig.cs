using HQ.Models.Interfaces;

namespace HQ.Plugins.WebSearch.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string WebSearchUrl { get; set; }
    public string WebSearchApiKey { get; set; }
}