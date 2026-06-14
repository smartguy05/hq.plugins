using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleWorkspace.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google OAuth 2.0 credentials for Drive, Docs and Sheets access")]
    public GoogleApiCredentials Credentials { get; set; }
}
