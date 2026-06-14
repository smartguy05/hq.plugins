using HQ.Models.Attributes;

namespace HQ.Plugins.Gusto.Models;

[OAuthProvider(
    "https://api.gusto.com/oauth/authorize",
    "https://api.gusto.com/oauth/token",
    "")]
public record GustoCredentials
{
    [OAuthClientId]
    [Tooltip("Gusto OAuth client ID (Developer portal)")]
    public string ClientId { get; init; }

    [OAuthClientSecret]
    [Sensitive]
    [Tooltip("Gusto OAuth client secret")]
    public string ClientSecret { get; init; }

    [OAuthUser]
    [Hidden]
    public string GustoUser { get; init; }

    [OAuthRefreshToken]
    [Hidden]
    public string RefreshToken { get; init; }
}
