using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Docs.v1;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using HQ.Plugins.GoogleWorkspace.Models;

namespace HQ.Plugins.GoogleWorkspace.Clients;

/// <summary>
/// Builds Drive/Docs/Sheets service clients from a single refresh-token credential.
/// Mirrors the OAuth flow used by HQ.Plugins.GoogleCalendar's CalService.
/// </summary>
public static class GoogleClientFactory
{
    private const string AppName = "Ai Orchestrator - Google Workspace Plugin";

    private static UserCredential BuildCredential(ServiceConfig config)
    {
        var creds = config.Credentials
                    ?? throw new InvalidOperationException("Google Workspace credentials are not configured.");

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = creds.ClientId,
                ClientSecret = creds.ClientSecret
            },
            Scopes =
            [
                DriveService.Scope.Drive,
                DocsService.Scope.Documents,
                SheetsService.Scope.Spreadsheets
            ]
        });

        return new UserCredential(flow, creds.GoogleUser ?? "user", new TokenResponse
        {
            RefreshToken = creds.RefreshToken
        });
    }

    public static DriveService CreateDrive(ServiceConfig config) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = BuildCredential(config),
            ApplicationName = AppName
        });

    public static DocsService CreateDocs(ServiceConfig config) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = BuildCredential(config),
            ApplicationName = AppName
        });

    public static SheetsService CreateSheets(ServiceConfig config) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = BuildCredential(config),
            ApplicationName = AppName
        });
}
