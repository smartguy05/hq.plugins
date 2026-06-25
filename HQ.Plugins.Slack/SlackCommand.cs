using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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
        var result = await this.ProcessRequest(RawServiceRequest, config, NotificationService);
        await Log(LogLevel.Info, $"Slack DoWork completed for method: {serviceRequest.Method}");
        return result;
    }

    [Display(Name = "send_slack_message")]
    [Description("Sends a message via Slack to a specified channel or DM. If no channel ID is provided, uses the configured notification channel.")]
    [Parameters(typeof(SendSlackMessageArgs))]
    public async Task<object> SendSlackMessage(ServiceConfig config, SendSlackMessageArgs args)
    {
        await Log(LogLevel.Info, $"SendSlackMessage called - ChannelId: {args.ChannelId}, MessageText length: {args.MessageText?.Length ?? 0}");
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        if (string.IsNullOrWhiteSpace(args.ChannelId))
        {
            args.ChannelId = config.NotificationChannelId;
        }

        var service = GetSlackService(config, NotificationService, Log);

        if (!string.IsNullOrWhiteSpace(args.FileContent) && !string.IsNullOrWhiteSpace(args.FileName))
        {
            var uploadResult = await service.UploadFile(
                args.FileContent,
                args.FileName,
                args.FileType,
                args.ChannelId);

            if (!string.IsNullOrWhiteSpace(args.MessageText))
            {
                await service.SendMessage(args.MessageText, args.ChannelId);
            }

            return uploadResult;
        }

        return await service.SendMessage(args.MessageText, args.ChannelId);
    }

    [Display(Name = "upload_slack_file")]
    [Description("Uploads a file to a Slack channel.")]
    [Parameters(typeof(UploadSlackFileArgs))]
    public async Task<object> UploadSlackFile(ServiceConfig config, UploadSlackFileArgs args)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.UploadFile(
            args.FileContent,
            args.FileName,
            args.FileType,
            args.ChannelId);
    }

    [Display(Name = "download_slack_file")]
    [Description("Downloads a file from Slack by its file ID and returns the content as base64.")]
    [Parameters(typeof(DownloadSlackFileArgs))]
    public async Task<object> DownloadSlackFile(ServiceConfig config, DownloadSlackFileArgs args)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.DownloadFile(args.FileId);
    }

    [Display(Name = "open_slack_dm")]
    [Description("Opens or resumes a direct message conversation with one or more Slack users. Returns the DM channel ID that can be used with send_slack_message. For a single user, opens a 1:1 DM. For multiple users, opens a group DM.")]
    [Parameters(typeof(OpenSlackDmArgs))]
    public async Task<object> OpenSlackDm(ServiceConfig config, OpenSlackDmArgs args)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.OpenConversation(args.UserIds);
    }

    [Display(Name = "list_slack_users")]
    [Description("Lists workspace users with their IDs, names, and display names. Use this to find user IDs for opening DMs.")]
    [Parameters(typeof(EmptyArgs))]
    public async Task<object> ListSlackUsers(ServiceConfig config)
    {
        if (string.IsNullOrEmpty(config.BotToken))
            throw new ArgumentException("Bot token is required");

        var service = GetSlackService(config, NotificationService, Log);

        return await service.ListUsers();
    }

    [Display(Name = "list_slack_channels")]
    [Description("Lists Slack channels the bot has access to.")]
    [Parameters(typeof(EmptyArgs))]
    public async Task<object> ListSlackChannels(ServiceConfig config)
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

            // Get botUserId via direct HTTP call — avoids forcing the builder's internal
            // Lazy<ISlackServiceProvider> before event handlers are registered.
            // When GetApiClient() is called first, it builds the provider eagerly;
            // subsequent handler registrations may not be picked up by the socket mode
            // client's event dispatcher, causing only the first agent to receive events.
            string botUserId;
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", config.BotToken);
                var authJson = await http.GetFromJsonAsync<JsonElement>("https://slack.com/api/auth.test");
                if (!authJson.GetProperty("ok").GetBoolean())
                {
                    var error = authJson.TryGetProperty("error", out var errProp) ? errProp.GetString() : "unknown";
                    throw new Exception($"Slack auth.test failed: {error}");
                }
                botUserId = authJson.GetProperty("user_id").GetString();
            }

            _myBotUserId = botUserId;
            await log(LogLevel.Info, $"Slack bot user ID: {botUserId} (agent: {config.AgentName ?? config.AgentId?.ToString()})");

            // Reverse map so outgoing paths can find the connection by AgentId
            if (config.AgentId.HasValue)
                AgentToBotUserMap[config.AgentId.Value] = botUserId;

            // If a Socket Mode client is already connected for this bot, reuse it.
            // Creating a second connection with the same app-level token causes Slack to
            // invalidate the first WebSocket, leading to a reconnection loop where neither works.
            if (ConnectionsByBotUser.TryGetValue(botUserId, out var existing) &&
                (existing.SocketModeClient?.Connected ?? false))
            {
                await log(LogLevel.Info,
                    $"Slack bot {botUserId} already connected (agent: {existing.Config?.AgentName ?? existing.AgentId?.ToString()}). " +
                    $"Reusing existing Socket Mode connection for agent {config.AgentName ?? config.AgentId?.ToString()}. " +
                    "Incoming messages will be handled by the first agent's listener.");
                return new { Success = true, Message = "Slack initialized (reusing existing connection)" };
            }

            var builder = new SlackNet.SlackServiceBuilder()
                .UseApiToken(config.BotToken)
                .UseAppLevelToken(config.AppLevelToken);

            // Register event handler factories BEFORE any builder.Get*() calls.
            // The builder's internal Lazy<ISlackServiceProvider> is forced on the first
            // Get*() call; registering handlers first ensures they're included when the
            // provider and its event dispatcher are built.
            SlackService service = null;
            builder.RegisterEventHandler<SlackNet.Events.MessageEvent>(ctx => service);
            builder.RegisterBlockActionHandler<SlackNet.Blocks.ButtonAction>(
                $"{SlackService.ConfirmationActionId}_0", ctx => service);
            builder.RegisterBlockActionHandler<SlackNet.Blocks.ButtonAction>(
                $"{SlackService.ConfirmationActionId}_1", ctx => service);

            // Store connection state keyed by the bot user ID — the stable connection identity
            var conn = ConnectionsByBotUser.GetOrAdd(botUserId, _ => new SlackConnectionState());
            conn.Config = config;
            conn.ConfirmationService = notificationService;
            conn.BotUserId = botUserId;
            conn.AgentId = config.AgentId;

            // Now safe to force the builder's lazy — handlers are already registered
            conn.ApiClient = builder.GetApiClient();

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

            // Create the service instance — the factory closures above capture 'service'
            // by reference, so they'll return this instance when the first event arrives.
            service = new SlackService(conn.ApiClient, log, config, notificationService,
                (confirmationId, value) => ConfirmForBotUser(confirmationId, value, botUserId), botUserId);
            conn.Service = service;

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
        if (request != null && request.AgentId.HasValue &&
            AgentToBotUserMap.TryGetValue(request.AgentId.Value, out var botUserId))
        {
            ConnectionsByBotUser.TryGetValue(botUserId, out conn);
        }

        // Fallback: single connection
        conn ??= ConnectionsByBotUser.Count == 1 ? ConnectionsByBotUser.Values.First() : null;

        if (conn?.ApiClient == null || conn.Config == null || conn.ConfirmationService == null)
        {
            throw new InvalidOperationException(
                $"SlackCommand is not fully initialized for agent {request?.AgentId}. " +
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
