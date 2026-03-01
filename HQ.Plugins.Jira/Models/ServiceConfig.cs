using HQ.Models.Interfaces;

namespace HQ.Plugins.Jira.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Domain { get; set; }
    public string Email { get; set; }
    public string ApiToken { get; set; }
}
