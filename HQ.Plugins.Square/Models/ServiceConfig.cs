using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Square.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Square access token (Developer Dashboard → Credentials). Use a sandbox token for development.")]
    public string AccessToken { get; set; }

    [Tooltip("Use the Square sandbox (connect.squareupsandbox.com) instead of production. Default true for development.")]
    public bool UseSandbox { get; set; } = true;

    [Tooltip("Default location ID used when a request omits one (most Square calls are location-scoped).")]
    public string DefaultLocationId { get; set; }

    [Tooltip("Require user confirmation before booking actions that notify customers (create/cancel booking). Default true.")]
    public bool RequiresConfirmation { get; set; } = true;
}
