using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.GoogleAnalytics.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google OAuth 2.0 credentials with the analytics.readonly scope")]
    public GoogleApiCredentials Credentials { get; set; }

    [Tooltip("Default GA4 property ID (numeric, e.g. 123456789) used when a request omits one")]
    public string DefaultPropertyId { get; set; }
}
