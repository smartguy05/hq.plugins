using HQ.Models.Attributes;

namespace HQ.Plugins.GoogleWorkspace.Models;

[OAuthProvider(
    "https://accounts.google.com/o/oauth2/v2/auth",
    "https://oauth2.googleapis.com/token",
    "https://www.googleapis.com/auth/drive https://www.googleapis.com/auth/documents https://www.googleapis.com/auth/spreadsheets")]
public record GoogleApiCredentials
{
    [OAuthClientId]
    [Tooltip("OAuth 2.0 Client ID from Google Cloud Console")]
    public string ClientId { get; init; }

    [OAuthClientSecret]
    [Sensitive]
    [Tooltip("OAuth 2.0 Client Secret from Google Cloud Console")]
    public string ClientSecret { get; init; }

    [OAuthUser]
    [Hidden]
    public string GoogleUser { get; init; }

    [OAuthRefreshToken]
    [Hidden]
    public string RefreshToken { get; init; }
}
