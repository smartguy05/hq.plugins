using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Slack.Models;

namespace HQ.Plugins.Slack;

public class SlackCommand : CommandBase<ServiceRequest, ServiceConfig>, INotificationPlugin
{
    public override string Name => "Slack";
    public override string Description => "A plugin to send and receive Slack messages";
    protected override INotificationService NotificationService { get; set; }
    private static SlackService _service;
    private static SlackNet.ISlackApiClient _apiClient;
    private static SlackNet.ISlackSocketModeClient _socketModeClient;
    private static ServiceConfig _config;
    private static INotificationService _staticConfirmationService;

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        await Log(LogLevel.Info, $"Slack DoWork called with method: {serviceRequest.Method}");
        var result = await this.ProcessRequest(serviceRequest, config, NotificationService);
        await Log(LogLevel.Info, $"Slack DoWork completed for method: {serviceRequest.Method}");
        return result;
    }

    [Display(Name = "send_slack_message")]
    [Description("Sends a message via Slack to a specified channel or DM. If no channel ID is provided, uses the configured notification channel.")]
    [Parameters("""{"type":"object","properties":{"channelId":{"type":"string","description":"The Slack channel or DM ID to send the message to. Optional, defaults to configured notification channel."},"messageText":{"type":"string","description":"The message text to send"},"fileContent":{"type":"string","description":"Optional base64-encoded file content to attach"},"fileName":{"type":"string","description":"Optional filename for the attachment"},"fileType":{"type":"string","description":"Optional MIME type for the attachment"}},"required":["messageText"]}""")]
    public async Task<object> SendSlackMessage(ServiceConfig config, ServiceRequest serviceRequest)
    {
        await Log(LogLevel.Info, $"SendSlackMessage called - ChannelId: {serviceRequest.ChannelId}, MessageText length: {serviceRequest.MessageText?.Length ?? 0}");
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        if (string.IsNullOrWhiteSpace(serviceRequest.ChannelId))
        {
            serviceRequest.ChannelId = config.NotificationChannelId;
        }

        _service = GetSlackService(config, NotificationService, Log);

        if (!string.IsNullOrWhiteSpace(serviceRequest.FileContent) && !string.IsNullOrWhiteSpace(serviceRequest.FileName))
        {
            var uploadResult = await _service.UploadFile(
                serviceRequest.FileContent,
                serviceRequest.FileName,
                serviceRequest.FileType,
                serviceRequest.ChannelId);

            if (!string.IsNullOrWhiteSpace(serviceRequest.MessageText))
            {
                await _service.SendMessage(serviceRequest.MessageText, serviceRequest.ChannelId);
            }

            return uploadResult;
        }

        return await _service.SendMessage(serviceRequest.MessageText, serviceRequest.ChannelId);
    }

    [Display(Name = "upload_slack_file")]
    [Description("Uploads a file to a Slack channel.")]
    [Parameters("""{"type":"object","properties":{"channelId":{"type":"string","description":"The Slack channel ID to upload the file to"},"fileContent":{"type":"string","description":"Base64-encoded file content"},"fileName":{"type":"string","description":"The filename"},"fileType":{"type":"string","description":"Optional MIME type for the file"}},"required":["channelId","fileContent","fileName"]}""")]
    public async Task<object> UploadSlackFile(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        _service = GetSlackService(config, NotificationService, Log);

        return await _service.UploadFile(
            serviceRequest.FileContent,
            serviceRequest.FileName,
            serviceRequest.FileType,
            serviceRequest.ChannelId);
    }

    [Display(Name = "download_slack_file")]
    [Description("Downloads a file from Slack by its file ID and returns the content as base64.")]
    [Parameters("""{"type":"object","properties":{"fileId":{"type":"string","description":"The Slack file ID (starts with F)"}},"required":["fileId"]}""")]
    public async Task<object> DownloadSlackFile(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        _service = GetSlackService(config, NotificationService, Log);

        return await _service.DownloadFile(serviceRequest.FileId);
    }

    [Display(Name = "list_slack_channels")]
    [Description("Lists Slack channels the bot has access to.")]
    [Parameters("""{"type":"object","properties":{}}""")]
    public async Task<object> ListSlackChannels(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        _service = GetSlackService(config, NotificationService, Log);

        return await _service.ListChannels();
    }

    public override async Task<object> Initialize(string configString, LogDelegate log, INotificationService notificationService)
    {
        NotificationService = notificationService;
        _staticConfirmationService = notificationService;
        await log(LogLevel.Info, "Initializing Slack");
        try
        {
            var config = configString.ReadPluginConfig<ServiceConfig>();

            if (string.IsNullOrWhiteSpace(config.BotToken) || string.IsNullOrWhiteSpace(config.AppLevelToken))
            {
                var missing = new List<string>();
                if (string.IsNullOrWhiteSpace(config.BotToken)) missing.Add("BotToken");
                if (string.IsNullOrWhiteSpace(config.AppLevelToken)) missing.Add("AppLevelToken");
                await log(LogLevel.Warning,
                    $"Slack plugin not configured — {string.Join(" and ", missing)} required. Skipping connection. " +
                    "Configure the plugin settings and re-initialize to enable Slack.");
                return null;
            }

            _config = config;

            var builder = new SlackNet.SlackServiceBuilder()
                .UseApiToken(config.BotToken)
                .UseAppLevelToken(config.AppLevelToken);

            _apiClient = builder.GetApiClient();
            _service = new SlackService(_apiClient, log, config, notificationService, Confirm);

            builder.RegisterEventHandler<SlackNet.Events.MessageEvent>(_service);
            builder.RegisterBlockActionHandler<SlackNet.Blocks.ButtonAction>(SlackService.ConfirmationActionId, _service);

            try
            {
                _socketModeClient = builder.GetSocketModeClient();
                await log(LogLevel.Info, "Slack Socket Mode client created, connecting...");
            }
            catch (Exception ex)
            {
                await log(LogLevel.Error, $"Failed to create Socket Mode client: {ex.Message}", ex);
                throw;
            }

            try
            {
                await _service.Connect(_socketModeClient);
                return new { Success = true, Message = "Slack initialized and connected" };
            }
            catch (Exception ex)
            {
                await log(LogLevel.Error, $"Failed during Connect: {ex.Message}", ex);
                throw;
            }
        }
        catch (Exception e)
        {
            await log(LogLevel.Error, "Error initializing Slack", e);
            throw;
        }
    }

    private SlackService GetSlackService(ServiceConfig config, INotificationService notificationService, LogDelegate log)
    {
        if (_service != null) return _service;

        _config ??= config;
        log ??= Log;
        NotificationService ??= notificationService;

        if (_apiClient == null)
        {
            var builder = new SlackNet.SlackServiceBuilder()
                .UseApiToken(config.BotToken)
                .UseAppLevelToken(config.AppLevelToken);
            _apiClient = builder.GetApiClient();
        }

        _service = new SlackService(_apiClient, log, config, notificationService, Confirm);
        return _service;
    }

    public Task<object> RequestConfirmation(Confirmation confirmation, OrchestratorRequest request)
    {
        if (_apiClient == null || _config == null || _staticConfirmationService == null)
        {
            throw new InvalidOperationException(
                $"SlackCommand is not fully initialized. Status: " +
                $"ApiClient is {(_apiClient == null ? "null" : "not null")}, " +
                $"Config is {(_config == null ? "null" : "not null")}, " +
                $"ConfirmationService is {(_staticConfirmationService == null ? "null" : "not null")}."
            );
        }

        SlackService.PendingConfirmation = confirmation;
        _service ??= new SlackService(_apiClient, Log, _config, NotificationService, Confirm);
        return _service.SendConfirmationMessage(confirmation, _config.NotificationChannelId);
    }

    public async ValueTask<object> Confirm(string confirmationId, bool confirm)
    {
        var guid = Guid.Parse(confirmationId);
        return await _staticConfirmationService.Confirm(guid, confirm);
    }

    public Task Dispose()
    {
        _service?.Dispose();
        _service = null;
        if (_socketModeClient?.Connected ?? false)
        {
            _socketModeClient.Disconnect();
        }
        _socketModeClient?.Dispose();
        _socketModeClient = null;
        _apiClient = null;
        _config = null;
        _staticConfirmationService = null;
        return Task.CompletedTask;
    }
}
