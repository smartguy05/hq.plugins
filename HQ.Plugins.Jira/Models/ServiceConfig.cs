using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Jira.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Your Jira Cloud domain, e.g. mycompany.atlassian.net")]
    public string Domain { get; set; }

    [Tooltip("Email address associated with the Jira API token")]
    public string Email { get; set; }

    [Tooltip("Jira API token. Generate at https://id.atlassian.net/manage-profile/security/api-tokens")]
    public string ApiToken { get; set; }
}
