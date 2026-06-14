using HQ.Models.Attributes;

namespace HQ.Plugins.QuickBooks.Models;

[OAuthProvider(
    "https://appcenter.intuit.com/connect/oauth2",
    "https://oauth.platform.intuit.com/oauth2/v1/tokens/bearer",
    "com.intuit.quickbooks.accounting")]
public record IntuitCredentials
{
    [OAuthClientId]
    [Tooltip("OAuth 2.0 Client ID from the Intuit Developer app")]
    public string ClientId { get; init; }

    [OAuthClientSecret]
    [Sensitive]
    [Tooltip("OAuth 2.0 Client Secret from the Intuit Developer app")]
    public string ClientSecret { get; init; }

    [OAuthUser]
    [Hidden]
    public string IntuitUser { get; init; }

    [OAuthRefreshToken]
    [Hidden]
    public string RefreshToken { get; init; }
}
