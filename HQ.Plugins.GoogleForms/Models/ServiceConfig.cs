using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleForms.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google OAuth 2.0 credentials with the forms.body and forms.responses.readonly scopes")]
    public GoogleApiCredentials Credentials { get; set; }
}
