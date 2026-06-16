using Azure.Core;
using Azure.Identity;
using HQ.Plugins.Microsoft365.Models;
using Microsoft.Graph;

namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>
/// Builds Microsoft Graph clients from app-only client-secret credentials.
/// Mirrors the credential setup in HQ.Plugins.Teams' TeamsGraphClient.
/// </summary>
public static class GraphClientFactory
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    public static ClientSecretCredential CreateCredential(ServiceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.TenantId) || string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.ClientSecret))
            throw new InvalidOperationException("Microsoft 365 credentials (TenantId, ClientId, ClientSecret) are not configured.");
        return new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
    }

    public static GraphServiceClient CreateGraph(ServiceConfig config) =>
        new(CreateCredential(config), Scopes);

    public static async Task<string> GetTokenAsync(ClientSecretCredential credential, CancellationToken ct = default)
    {
        var token = await credential.GetTokenAsync(new TokenRequestContext(Scopes), ct);
        return token.Token;
    }
}
