using System.Collections.Concurrent;
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

    /// <summary>
    /// Per-connection state keyed by bot user ID (from auth.test). This is the stable identity
    /// of a Slack connection — it doesn't depend on mutable instance fields or agent context
    /// that may not be available during incoming Socket Mode events.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SlackConnectionState> ConnectionsByBotUser = new();

    /// <summary>
    /// Reverse lookup: AgentId → botUserId, so outgoing paths (tool calls, confirmations)
    /// that only have an AgentId can find the right connection.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, string> AgentToBotUserMap = new();

    private class SlackConnectionState
    {
        public SlackService Service;
        public SlackNet.ISlackApiClient ApiClient;
        public SlackNet.ISlackSocketModeClient SocketModeClient;
        public ServiceConfig Config;
        public INotificationService ConfirmationService;
        public string BotUserId;
        public Guid? AgentId;
    }

    /// <summary>
    /// The bot user ID for the connection this instance initialized. Set once in Initialize(),
    /// used in Dispose() to find the right connection to tear down.
    /// </summary>
    private string _myBotUserId;

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

        var service = GetSlackService(config, NotificationService, Log);

        if (!string.IsNullOrWhiteSpace(serviceRequest.FileContent) && !string.IsNullOrWhiteSpace(serviceRequest.FileName))
        {
            var uploadResult = await service.UploadFile(
                serviceRequest.FileContent,
                serviceRequest.FileName,
                serviceRequest.FileType,
                serviceRequest.ChannelId);

            if (!string.IsNullOrWhiteSpace(serviceRequest.MessageText))
            {
                await service.SendMessage(serviceRequest.MessageText, serviceRequest.ChannelId);
            }

            return uploadResult;
        }

        return await service.SendMessage(serviceRequest.MessageText, serviceRequest.ChannelId);
    }

    [Display(Name = "upload_slack_file")]
    [Description("Uploads a file to a Slack channel.")]
    [Parameters("""{"type":"object","properties":{"channelId":{"type":"string","description":"The Slack channel ID to upload the file to"},"fileContent":{"type":"string","description":"Base64-encoded file content"},"fileName":{"type":"string","description":"The filename"},"fileType":{"type":"string","description":"Optional MIME type for the file"}},"required":["channelId","fileContent","fileName"]}""")]
    public async Task<object> UploadSlackFile(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.UploadFile(
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

        var service = GetSlackService(config, NotificationService, Log);

        return await service.DownloadFile(serviceRequest.FileId);
    }

    [Display(Name = "open_slack_dm")]
    [Description("Opens or resumes a direct message conversation with one or more Slack users. Returns the DM channel ID that can be used with send_slack_message. For a single user, opens a 1:1 DM. For multiple users, opens a group DM.")]
    [Parameters("""{"type":"object","properties":{"userIds":{"type":"string","description":"Comma-separated Slack user IDs to open a DM with"}},"required":["userIds"]}""")]
    public async Task<object> OpenSlackDm(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.OpenConversation(serviceRequest.UserIds);
    }

    [Display(Name = "list_slack_users")]
    [Description("Lists workspace users with their IDs, names, and display names. Use this to find user IDs for opening DMs.")]
    [Parameters("""{"type":"object","properties":{}}""")]
    public async Task<object> ListSlackUsers(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.ListUsers();
    }

    [Display(Name = "list_slack_channels")]
    [Description("Lists Slack channels the bot has access to.")]
    [Parameters("""{"type":"object","properties":{}}""")]
    public async Task<object> ListSlackChannels(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.ListChannels();
    }

    public override async Task<object> Initialize(string configString, LogDelegate log, INotificationService notificationService)
    {
        NotificationService = notificationService;
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

            var builder = new SlackNet.SlackServiceBuilder()
                .UseApiToken(config.BotToken)
                .UseAppLevelToken(config.AppLevelToken);

            var apiClient = builder.GetApiClient();

            var authResponse = await apiClient.Auth.Test();
            var botUserId = authResponse.UserId;
            _myBotUserId = botUserId;
            await log(LogLevel.Info, $"Slack bot user ID: {botUserId} (agent: {config.AgentId})");

            // Store connection state keyed by the bot user ID — the stable connection identity
            var conn = ConnectionsByBotUser.GetOrAdd(botUserId, _ => new SlackConnectionState());
            conn.ApiClient = apiClient;
            conn.Config = config;
            conn.ConfirmationService = notificationService;
            conn.BotUserId = botUserId;
            conn.AgentId = config.AgentId;

            // Reverse map so outgoing paths can find the connection by AgentId
            if (config.AgentId.HasValue)
                AgentToBotUserMap[config.AgentId.Value] = botUserId;

            conn.Service = new SlackService(apiClient, log, config, notificationService,
                (confirmationId, value) => ConfirmForBotUser(confirmationId, value, botUserId), botUserId);

            builder.RegisterEventHandler<SlackNet.Events.MessageEvent>(conn.Service);
            builder.RegisterBlockActionHandler<SlackNet.Blocks.ButtonAction>($"{SlackService.ConfirmationActionId}_0", conn.Service);
            builder.RegisterBlockActionHandler<SlackNet.Blocks.ButtonAction>($"{SlackService.ConfirmationActionId}_1", conn.Service);

            try
            {
                conn.SocketModeClient = builder.GetSocketModeClient();
                await log(LogLevel.Info, "Slack Socket Mode client created, connecting...");
            }
            catch (Exception ex)
            {
                await log(LogLevel.Error, $"Failed to create Socket Mode client: {ex.Message}", ex);
                throw;
            }

            try
            {
                await conn.Service.Connect(conn.SocketModeClient);
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

    /// <summary>
    /// Finds the connection state for a given config, using AgentId → botUserId reverse lookup.
    /// </summary>
    private SlackConnectionState FindConnection(ServiceConfig config)
    {
        // Try AgentId reverse lookup first
        if (config.AgentId.HasValue &&
            AgentToBotUserMap.TryGetValue(config.AgentId.Value, out var botUserId) &&
            ConnectionsByBotUser.TryGetValue(botUserId, out var conn))
        {
            return conn;
        }

        // Fallback: if there's only one connection, use it
        if (ConnectionsByBotUser.Count == 1)
        {
            return ConnectionsByBotUser.Values.First();
        }

        return null;
    }

    private SlackService GetSlackService(ServiceConfig config, INotificationService notificationService, LogDelegate log)
    {
        var conn = FindConnection(config);
        if (conn?.Service != null) return conn.Service;

        // No existing connection — create a minimal one for outgoing-only use
        conn ??= new SlackConnectionState();
        conn.Config ??= config;
        log ??= Log;
        NotificationService ??= notificationService;

        if (conn.ApiClient == null)
        {
            var builder = new SlackNet.SlackServiceBuilder()
                .UseApiToken(config.BotToken)
                .UseAppLevelToken(config.AppLevelToken);
            conn.ApiClient = builder.GetApiClient();
        }

        conn.Service = new SlackService(conn.ApiClient, log, config, notificationService,
            (confirmationId, value) => ConfirmForBotUser(confirmationId, value, conn.BotUserId), conn.BotUserId);
        return conn.Service;
    }

    public Task<object> RequestConfirmation(Confirmation confirmation, OrchestratorRequest request)
    {
        // Find connection via AgentId from the orchestrator request
        SlackConnectionState conn = null;
        if (request.AgentId.HasValue &&
            AgentToBotUserMap.TryGetValue(request.AgentId.Value, out var botUserId))
        {
            ConnectionsByBotUser.TryGetValue(botUserId, out conn);
        }

        // Fallback: single connection
        conn ??= ConnectionsByBotUser.Count == 1 ? ConnectionsByBotUser.Values.First() : null;

        if (conn?.ApiClient == null || conn.Config == null || conn.ConfirmationService == null)
        {
            throw new InvalidOperationException(
                $"SlackCommand is not fully initialized for agent {request.AgentId}. " +
                "Ensure Initialize() has been called for this agent."
            );
        }

        conn.Service ??= new SlackService(conn.ApiClient, Log, conn.Config, NotificationService,
            (confirmationId, value) => ConfirmForBotUser(confirmationId, value, conn.BotUserId), conn.BotUserId);
        conn.Service.PendingConfirmation = confirmation;
        return conn.Service.SendConfirmationMessage(confirmation, conn.Config.NotificationChannelId);
    }

    public async ValueTask<object> Confirm(string confirmationId, bool confirm)
    {
        // Direct Confirm (not via closure) — try any available connection
        return await ConfirmForBotUser(confirmationId, confirm, _myBotUserId);
    }

    private async ValueTask<object> ConfirmForBotUser(string confirmationId, bool confirm, string botUserId)
    {
        var guid = Guid.Parse(confirmationId);

        if (botUserId != null &&
            ConnectionsByBotUser.TryGetValue(botUserId, out var conn) &&
            conn.ConfirmationService != null)
        {
            return await conn.ConfirmationService.Confirm(guid, confirm);
        }

        // Fallback: search all connections
        foreach (var kvp in ConnectionsByBotUser)
        {
            if (kvp.Value.ConfirmationService != null)
            {
                return await kvp.Value.ConfirmationService.Confirm(guid, confirm);
            }
        }

        throw new InvalidOperationException("No confirmation service available");
    }

    public Task Dispose()
    {
        // Use the bot user ID stored during Initialize — no dependency on mutable agent state
        if (_myBotUserId != null && ConnectionsByBotUser.TryRemove(_myBotUserId, out var conn))
        {
            // Also clean up the reverse map
            if (conn.AgentId.HasValue)
                AgentToBotUserMap.TryRemove(conn.AgentId.Value, out _);

            conn.Service?.Dispose();
            conn.Service = null;
            if (conn.SocketModeClient?.Connected ?? false)
            {
                conn.SocketModeClient.Disconnect();
            }
            conn.SocketModeClient?.Dispose();
            conn.SocketModeClient = null;
            conn.ApiClient = null;
            conn.Config = null;
            conn.ConfirmationService = null;
        }
        return Task.CompletedTask;
    }
}
