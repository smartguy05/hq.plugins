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
    private TeamsService _service;
    private TeamsGraphClient _graphClient;
    private TeamsBot _bot;
    private ServiceConfig _config;
    private INotificationService _staticConfirmationService;
    private HttpListener _httpListener;

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(RawServiceRequest, config, NotificationService);
    }

    [Display(Name = "send_teams_message")]
    [Description("Sends a message to a Microsoft Teams channel. If no team/channel ID is provided, uses the configured notification channel.")]
    [Parameters(typeof(SendTeamsMessageArgs))]
    public async Task<object> SendTeamsMessage(ServiceConfig config, SendTeamsMessageArgs request)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.SendMessage(request.MessageText, request.TeamId, request.ChannelId);
    }

    [Display(Name = "list_teams")]
    [Description("Lists Microsoft Teams teams the app has access to.")]
    [Parameters(typeof(EmptyArgs))]
    public async Task<object> ListTeams(ServiceConfig config, EmptyArgs request)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.ListTeams();
    }

    [Display(Name = "list_teams_channels")]
    [Description("Lists channels in a Microsoft Teams team.")]
    [Parameters(typeof(ListTeamsChannelsArgs))]
    public async Task<object> ListTeamsChannels(ServiceConfig config, ListTeamsChannelsArgs request)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.ListChannels(request.TeamId);
    }

    [Display(Name = "send_teams_file")]
    [Description("Uploads a file to a Microsoft Teams channel's SharePoint folder.")]
    [Parameters(typeof(SendTeamsFileArgs))]
    public async Task<object> SendTeamsFile(ServiceConfig config, SendTeamsFileArgs request)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.UploadFile(
            request.FileContent,
            request.FileName,
            request.FileType,
            request.TeamId,
            request.ChannelId);
    }

    [Display(Name = "download_teams_file")]
    [Description("Downloads a file from Teams/SharePoint by drive item ID and returns the content as base64.")]
    [Parameters(typeof(DownloadTeamsFileArgs))]
    public async Task<object> DownloadTeamsFile(ServiceConfig config, DownloadTeamsFileArgs request)
    {
        if (string.IsNullOrEmpty(config.ClientId))
            throw new ArgumentException("Azure AD ClientId is required");

        _service = GetTeamsService(config);

        return await _service.DownloadFile(request.DriveItemId);
    }

    /// <summary>
    /// WP6B-1 test/observability seam: true once the Bot Framework HTTP listener is actively
    /// listening. False when Initialize refused to start it (blank BotAppId/BotAppPassword) or
    /// before/after it runs.
    /// </summary>
    public bool IsListenerActive => _httpListener?.IsListening ?? false;

    /// <summary>WP6B-1 test/observability seam: the bound HttpListener prefixes, if any.</summary>
    public IReadOnlyCollection<string> ListenerPrefixes =>
        _httpListener?.Prefixes?.ToList() ?? new List<string>();

    public override async Task<object> Initialize(string configString, LogDelegate log, INotificationService notificationService)
    {
        NotificationService ??= notificationService;
        _staticConfirmationService = notificationService;
        // Execute() (the tool-call path) sets the base Logger field, but RequestConfirmation can
        // run before any tool call ever executes (e.g. a scheduled/proactive confirmation), so
        // Log(...) needs a Logger set from Initialize too, not just from Execute.
        Logger ??= log;
        await log(LogLevel.Info, "Initializing Teams");
        try
        {
            var config = configString.ReadPluginConfig<ServiceConfig>();
            _config = config;

            _graphClient = new TeamsGraphClient(config, log);
            _service = new TeamsService(_graphClient, log, config);
            _bot = new TeamsBot(log, config, notificationService, Confirm, _graphClient);

            // WP6B-1: Microsoft.Bot.Connector.Authentication.SimpleCredentialProvider treats a
            // blank BotAppId as "auth disabled" (IsAuthenticationDisabledAsync() returns true),
            // which would let ProcessBotFrameworkRequest run inbound activities through the
            // orchestrator with NO Bot Framework authentication at all. Refuse to open the
            // listener at all unless both credentials are configured, rather than silently
            // running unauthenticated.
            if (string.IsNullOrWhiteSpace(config.BotAppId) || string.IsNullOrWhiteSpace(config.BotAppPassword))
            {
                await log(LogLevel.Warning,
                    "Teams BotAppId/BotAppPassword are not configured; refusing to start the inbound " +
                    "HTTP listener because Bot Framework request validation self-disables for a blank " +
                    "AppId (WP6B-1). Outbound Teams tools (send/list/upload/download) remain available.");
                return new
                {
                    Success = true,
                    Message = "Teams plugin initialized (inbound listener disabled: BotAppId/BotAppPassword not configured)"
                };
            }

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

        // WP6B-1: bind loopback only, not "+" (all interfaces). The listener has no ASP.NET
        // pipeline in front of it (no rate limiting, no request size limit, no security
        // headers), so it should never be reachable directly from outside the host; anything
        // that needs to expose it does so deliberately via an explicit reverse-proxy/port
        // mapping, not by default.
        var prefix = $"http://127.0.0.1:{config.ListenerPort}{config.ListenerPath}/";
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

            // WP6B-1: an absent Authorization header must be rejected outright, never passed
            // through as string.Empty. Initialize() already refuses to start this listener
            // unless BotAppId/BotAppPassword are both configured, so SimpleCredentialProvider
            // below always requires real Bot Framework validation — but a missing header is an
            // unambiguous "unauthenticated caller" and there is no reason to let it reach the
            // adapter (which would otherwise fail later, but only after deserializing the body).
            var authHeader = context.Request.Headers["Authorization"];
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                await log(LogLevel.Warning, "Rejected Teams Bot Framework request with no Authorization header");
                context.Response.StatusCode = 401;
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
                authHeader,
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

        _config = config ?? _config;
        _graphClient ??= new TeamsGraphClient(config, Log);
        _service = new TeamsService(_graphClient, Log, config);
        return _service;
    }

    /// <summary>Test/observability seam: whether a confirmation is pending for <paramref name="channelKey"/>.</summary>
    public bool HasPendingConfirmationForChannel(string channelKey) => _bot?.HasPendingConfirmation(channelKey) ?? false;

    public async Task<object> RequestConfirmation(Confirmation confirmation, OrchestratorRequest request)
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

        _service ??= new TeamsService(_graphClient, Log, _config);
        var result = await _service.SendConfirmationCard(confirmation, _config.NotificationTeamId, _config.NotificationChannelId);

        // WP6B-4 review follow-up: NotificationChannelId is optional (a Teams bot used only in
        // 1:1 chat never sets it), and TeamsGetChannelId() returns null for every 1:1 chat --
        // both coalesce to the same "" key in PendingConfirmationStore. Binding the pending
        // confirmation to that key regardless of whether SendConfirmationCard actually posted
        // anywhere meant any stranger who has ever DMed the bot could approve a confirmation
        // nobody actually saw. Only bind once we know (a) a real channel was configured and
        // (b) the card was actually posted there.
        var posted = string.IsNullOrWhiteSpace(_config.NotificationTeamId) ||
                     string.IsNullOrWhiteSpace(_config.NotificationChannelId)
            ? false
            : IsSuccess(result);

        if (posted)
        {
            _bot.SetPendingConfirmation(confirmation, _config.NotificationChannelId);
        }
        else
        {
            await Log(LogLevel.Warning,
                "Teams confirmation card was not posted to a configured channel (NotificationTeamId/" +
                "NotificationChannelId unset, or the send failed); refusing to record a pending " +
                "confirmation, since the blank channel key would otherwise collide with every 1:1 " +
                "chat with this bot (WP6B-4).");
        }

        return result;
    }

    private static bool IsSuccess(object result) =>
        result?.GetType().GetProperty("Success")?.GetValue(result) is true;

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
