using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Notion.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Notion integration token (Internal Integration Secret, or OAuth access token)")]
    public string AccessToken { get; set; }

    [Tooltip("Notion API version header (default 2022-06-28)")]
    public string NotionVersion { get; set; }
}
