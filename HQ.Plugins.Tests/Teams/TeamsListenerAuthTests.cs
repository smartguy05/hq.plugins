using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams;
using HQ.Plugins.Teams.Models;
using Moq;

namespace HQ.Plugins.Tests.Teams;

/// <summary>
/// WP6B-1: the Teams plugin used to open a private HttpListener whose Bot Framework auth
/// self-disables when BotAppId/BotAppPassword are blank (SimpleCredentialProvider.
/// IsAuthenticationDisabledAsync() returns true), and it passed an absent Authorization header
/// through as string.Empty instead of rejecting the request, and it bound "http://+:{port}"
/// (all interfaces) instead of loopback. This fixes and pins all three behaviours.
/// </summary>
public class TeamsListenerAuthTests
{
    private static Task NoopLog(HQ.Models.Enums.LogLevel level, string message, Exception ex = null) => Task.CompletedTask;

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static ServiceConfig MakeConfig(int port, string botAppId = "app-id", string botAppPassword = "app-password") => new()
    {
        Name = "Teams",
        ClientId = "client-id",
        ClientSecret = "client-secret",
        TenantId = "tenant-id",
        BotAppId = botAppId,
        BotAppPassword = botAppPassword,
        ListenerPort = port,
        ListenerPath = "/api/messages",
        AiPlugin = "OpenAi"
    };

    [Theory]
    [InlineData(null, "password")]
    [InlineData("", "password")]
    [InlineData("appid", null)]
    [InlineData("appid", "")]
    [InlineData(null, null)]
    public async Task Initialize_WithBlankBotFrameworkCredentials_DoesNotStartListener(string botAppId, string botAppPassword)
    {
        var command = new TeamsCommand();
        var config = MakeConfig(GetFreeTcpPort(), botAppId, botAppPassword);
        var configJson = JsonSerializer.Serialize(config);

        await command.Initialize(configJson, NoopLog, Mock.Of<INotificationService>());

        try
        {
            Assert.False(command.IsListenerActive);
        }
        finally
        {
            await command.Dispose();
        }
    }

    [Fact]
    public async Task Initialize_WithBotFrameworkCredentials_StartsListenerOnLoopbackOnly()
    {
        var command = new TeamsCommand();
        var config = MakeConfig(GetFreeTcpPort());
        var configJson = JsonSerializer.Serialize(config);

        await command.Initialize(configJson, NoopLog, Mock.Of<INotificationService>());

        try
        {
            Assert.True(command.IsListenerActive);
            Assert.Contains(command.ListenerPrefixes, p => p.Contains("127.0.0.1"));
            Assert.DoesNotContain(command.ListenerPrefixes, p => p.Contains("://+:") || p.Contains("://*:"));
        }
        finally
        {
            await command.Dispose();
        }
    }

    [Fact]
    public async Task ProcessBotFrameworkRequest_WithoutAuthorizationHeader_Returns401AndNeverReachesAdapter()
    {
        var port = GetFreeTcpPort();
        var command = new TeamsCommand();
        var config = MakeConfig(port);
        await command.Initialize(JsonSerializer.Serialize(config), NoopLog, Mock.Of<INotificationService>());

        try
        {
            using var http = new HttpClient();
            var response = await http.PostAsync(
                $"http://127.0.0.1:{port}/api/messages/",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await command.Dispose();
        }
    }

    [Fact]
    public async Task ProcessBotFrameworkRequest_WithBlankAuthorizationHeader_Returns401()
    {
        var port = GetFreeTcpPort();
        var command = new TeamsCommand();
        var config = MakeConfig(port);
        await command.Initialize(JsonSerializer.Serialize(config), NoopLog, Mock.Of<INotificationService>());

        try
        {
            using var http = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/api/messages/")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Authorization", "");
            var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            await command.Dispose();
        }
    }
}
