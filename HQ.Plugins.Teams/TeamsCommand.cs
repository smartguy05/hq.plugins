using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Teams.Models;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Connector.Authentication;

namespace HQ.Plugins.Teams;

public class TeamsCommand : CommandBase<ServiceRequest, ServiceConfig>, INotificationPlugin
{
    public override string Name => "Teams";
    public override string Description => "A plugin to send and receive Microsoft Teams messages";
    protected override INotificationService NotificationService { get; set; }
    private static TeamsService _service;
    private static TeamsGraphClient _graphClient;
    private static TeamsBot _bot;
    private static ServiceConfig _config;
    private static INotificationService _staticConfirmationService;
    private static HttpListener _httpListener;

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    [Display(Name = "send_teams_message")]
    [Description("Sends a message to a Microsoft Teams channel. If no team/channel ID is provided, uses the configured notification channel.")]
    [Parameters("""{"type":"object","properties":{"messageText":{"type":"string","description":"The message text to send"},"teamId":{"type":"string","description":"The Teams team ID. Optional, defaults to configured notification team."},"channelId":{"type":"string","description":"The Teams channel ID. Optional, defaults to configured notification channel."}},"required":["messageText"]}""")]
    public async Task<object> SendTeamsMessage(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.SendMessage(serviceRequest.MessageText, serviceRequest.TeamId, serviceRequest.ChannelId);
    }

    [Display(Name = "list_teams")]
    [Description("Lists Microsoft Teams teams the app has access to.")]
    [Parameters("""{"type":"object","properties":{}}""")]
    public async Task<object> ListTeams(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.ListTeams();
    }

    [Display(Name = "list_teams_channels")]
    [Description("Lists channels in a Microsoft Teams team.")]
    [Parameters("""{"type":"object","properties":{"teamId":{"type":"string","description":"The Teams team ID to list channels for"}},"required":["teamId"]}""")]
    public async Task<object> ListTeamsChannels(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.ListChannels(serviceRequest.TeamId);
    }

    [Display(Name = "send_teams_file")]
    [Description("Uploads a file to a Microsoft Teams channel's SharePoint folder.")]
    [Parameters("""{"type":"object","properties":{"teamId":{"type":"string","description":"The Teams team ID"},"channelId":{"type":"string","description":"The Teams channel ID"},"fileContent":{"type":"string","description":"Base64-encoded file content"},"fileName":{"type":"string","description":"The filename"},"fileType":{"type":"string","description":"Optional MIME type for the file"}},"required":["teamId","channelId","fileContent","fileName"]}""")]
    public async Task<object> SendTeamsFile(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.UploadFile(
            serviceRequest.FileContent,
            serviceRequest.FileName,
            serviceRequest.FileType,
            serviceRequest.TeamId,
            serviceRequest.ChannelId);
    }

    [Display(Name = "download_teams_file")]
    [Description("Downloads a file from Teams/SharePoint by drive item ID and returns the content as base64.")]
    [Parameters("""{"type":"object","properties":{"driveItemId":{"type":"string","description":"The drive item ID in format 'driveId/itemId'"}},"required":["driveItemId"]}""")]
    public async Task<object> DownloadTeamsFile(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.DownloadFile(serviceRequest.DriveItemId);
    }

    public override async Task<object> Initialize(string configString, LogDelegate log, INotificationService notificationService)
    {
        NotificationService ??= notificationService;
        _staticConfirmationService = notificationService;
        await log(LogLevel.Info, "Initializing Teams");
        try
        {
            var config = configString.ReadPluginConfig<ServiceConfig>();
            _config ??= config;

            _graphClient ??= new TeamsGraphClient(config, log);
            _service = new TeamsService(_graphClient, log, config);
            _bot = new TeamsBot(log, config, notificationService, Confirm, _graphClient);

            // Start embedded HTTP listener for Bot Framework messages
            await StartHttpListener(config, log);

            return new { Success = true, Message = "Teams plugin initialized" };
        }
        catch (Exception e)
        {
            await log(LogLevel.Error, "Error initializing Teams", e);
            throw;
        }
    }

    private async Task StartHttpListener(ServiceConfig config, LogDelegate log)
    {
        if (_httpListener != null) return;

        var prefix = $"http://+:{config.ListenerPort}{config.ListenerPath}/";
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add(prefix);

        try
        {
            _httpListener.Start();
            await log(LogLevel.Info, $"Teams HTTP listener started on port {config.ListenerPort}");

            // Run the listener loop in the background
            _ = Task.Run(async () =>
            {
                while (_httpListener.IsListening)
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        await ProcessBotFrameworkRequest(context, config, log);
                    }
                    catch (HttpListenerException)
                    {
                        // Listener was stopped
                        break;
                    }
                    catch (Exception e)
                    {
                        await log(LogLevel.Error, $"Teams HTTP listener error: {e.Message}", e);
                    }
                }
            });
        }
        catch (Exception e)
        {
            await log(LogLevel.Error, $"Failed to start Teams HTTP listener: {e.Message}", e);
            _httpListener = null;
            throw;
        }
    }

    private async Task ProcessBotFrameworkRequest(HttpListenerContext context, ServiceConfig config, LogDelegate log)
    {
        try
        {
            if (context.Request.HttpMethod != "POST")
            {
                context.Response.StatusCode = 405;
                context.Response.Close();
                return;
            }

            using var reader = new System.IO.StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();

            var activity = JsonSerializer.Deserialize<Microsoft.Bot.Schema.Activity>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (activity is null)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                return;
            }

            // Create a TurnContext and process through the bot
#pragma warning disable CS0618 // BotFrameworkAdapter is simpler for embedded HttpListener usage
            var credentialProvider = new SimpleCredentialProvider(config.BotAppId, config.BotAppPassword);
            var adapter = new BotFrameworkAdapter(credentialProvider);
#pragma warning restore CS0618

            await adapter.ProcessActivityAsync(
                context.Request.Headers["Authorization"] ?? string.Empty,
                activity,
                async (turnContext, cancellationToken) =>
                {
                    await _bot.OnTurnAsync(turnContext, cancellationToken);
                },
                CancellationToken.None);

            context.Response.StatusCode = 200;
            context.Response.Close();
        }
        catch (Exception e)
        {
            await log(LogLevel.Error, $"Error processing Bot Framework request: {e.Message}", e);
            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    private TeamsService GetTeamsService(ServiceConfig config)
    {
        if (_service != null) return _service;

        _config ??= config;
        _graphClient ??= new TeamsGraphClient(config, Log);
        _service = new TeamsService(_graphClient, Log, config);
        return _service;
    }

    public Task<object> RequestConfirmation(Confirmation confirmation, OrchestratorRequest request)
    {
        if (_graphClient == null || _config == null || _staticConfirmationService == null)
        {
            throw new InvalidOperationException(
                $"TeamsCommand is not fully initialized. Status: " +
                $"GraphClient is {(_graphClient == null ? "null" : "not null")}, " +
                $"Config is {(_config == null ? "null" : "not null")}, " +
                $"ConfirmationService is {(_staticConfirmationService == null ? "null" : "not null")}."
            );
        }

        TeamsBot.PendingConfirmation = confirmation;
        _service ??= new TeamsService(_graphClient, Log, _config);
        return _service.SendConfirmationCard(confirmation, _config.NotificationTeamId, _config.NotificationChannelId);
    }

    public async ValueTask<object> Confirm(string confirmationId, bool confirm)
    {
        var guid = Guid.Parse(confirmationId);
        return await _staticConfirmationService.Confirm(guid, confirm);
    }

    public Task Dispose()
    {
        try
        {
            _httpListener?.Stop();
            _httpListener?.Close();
        }
        catch
        {
            // Ignore cleanup errors
        }

        _httpListener = null;
        return Task.CompletedTask;
    }
}
