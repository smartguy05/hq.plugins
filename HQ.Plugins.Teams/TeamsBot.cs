using System.Text.Json;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Chat;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams.Models;
using HQ.Services;
using HQ.Services.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Schema;

namespace HQ.Plugins.Teams;

public class TeamsBot : TeamsActivityHandler
{
    public static readonly Dictionary<string, ConversationReference> ConversationReferences = new();

    // WP6B-4: instance-scoped (one per agent's Teams config), keyed by the Teams channel id the
    // confirmation was posted to — replaces the old process-global `static Confirmation
    // PendingConfirmation` slot. See PendingConfirmationStore.
    private readonly PendingConfirmationStore _pendingConfirmations = new();

    private readonly LogDelegate _logger;
    private readonly ServiceConfig _config;
    private readonly INotificationService _notificationService;
    private readonly Func<string, bool, ValueTask<object>> _confirm;
    private readonly TeamsGraphClient _graphClient;

    public TeamsBot(
        LogDelegate logger,
        ServiceConfig config,
        INotificationService notificationService,
        Func<string, bool, ValueTask<object>> confirm,
        TeamsGraphClient graphClient)
    {
        _logger = logger;
        _config = config;
        _notificationService = notificationService;
        _confirm = confirm;
        _graphClient = graphClient;
    }

    /// <summary>
    /// WP6B-4: records the confirmation that was just posted to <paramref name="channelId"/>
    /// (the Teams channel id, e.g. <c>ServiceConfig.NotificationChannelId</c>) so an inbound
    /// reply or Adaptive Card action can only approve it if it arrives on that same channel.
    /// </summary>
    public void SetPendingConfirmation(Confirmation confirmation, string channelId) =>
        _pendingConfirmations.Set(channelId, confirmation);

    /// <summary>Test/observability seam: whether a confirmation is pending for <paramref name="channelKey"/>.</summary>
    public bool HasPendingConfirmation(string channelKey) => _pendingConfirmations.TryGet(channelKey, out _);

    protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
    {
        try
        {
            // Store conversation reference for proactive messaging
            var conversationReference = turnContext.Activity.GetConversationReference();
            ConversationReferences[conversationReference.Conversation.Id] = conversationReference;

            var messageText = turnContext.Activity.Text ?? string.Empty;
            var conversationId = $"teams-{turnContext.Activity.Conversation.Id}";

            await _logger(LogLevel.Info, $"Teams received message: '{messageText}'");

            // Handle special commands
            if (await ProcessSpecialCommands(turnContext, conversationId, messageText, cancellationToken))
                return;

            // Check for pending confirmations (text-based). WP6B-4: only a reply on the same
            // Teams channel the confirmation was posted to can ever match — a different
            // channel/team, or an unrelated 1:1 chat with this bot, gets no channel key match
            // (TeamsGetChannelId() is null/empty there) and so can never approve it.
            var channelKey = turnContext.Activity.TeamsGetChannelId();
            if (_pendingConfirmations.TryGet(channelKey, out var pendingConfirmation) &&
                _notificationService.DoesConfirmationExist(pendingConfirmation.Id ?? Guid.Empty, out _))
            {
                var lowerMessage = messageText.ToLowerInvariant();
                if (pendingConfirmation.Options.Any(a =>
                        string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase)))
                {
                    var value = pendingConfirmation.Options
                        .First(a => string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase))
                        .Value;
                    var confirmationResult = await _confirm(pendingConfirmation.Id.ToString(), value);
                    await SendConfirmationResult(turnContext, confirmationResult, cancellationToken);
                    _pendingConfirmations.Remove(channelKey);
                    return;
                }

                _pendingConfirmations.Remove(channelKey);
            }

            // Send typing indicator
            try { await turnContext.SendActivityAsync(new Activity { Type = ActivityTypes.Typing }, cancellationToken); }
            catch { /* best-effort typing indicator */ }

            // Download file attachments
            string fileBase64 = null;
            var attachments = turnContext.Activity.Attachments?
                .Where(a => a.ContentUrl != null && !a.ContentType.StartsWith("application/vnd.microsoft.card"))
                .ToList();
            if (attachments is { Count: > 0 })
            {
                var lastAttachment = attachments.Last();

                // WP6B-8: refuse to fetch a contentUrl that would reach an internal/loopback/
                // cloud-metadata address (SSRF) before ever making the outbound request.
                if (!TeamsAttachmentValidator.IsAttachmentUrlAllowed(lastAttachment.ContentUrl, out var blockReason))
                {
                    await _logger(LogLevel.Warning,
                        $"Refusing to download Teams attachment: {blockReason}");
                }
                else
                {
                    try
                    {
                        fileBase64 = await DownloadAttachmentSafelyAsync(lastAttachment.ContentUrl);
                    }
                    catch (Exception e)
                    {
                        await _logger(LogLevel.Error, $"Failed to download Teams attachment: {e.Message}", e);
                    }
                }
            }

            // Route to orchestrator
            var serviceRequest = new
            {
                SystemPrompt = (string)null,
                UserPrompt = messageText,
                ConversationId = conversationId,
                Photo = fileBase64
            };
            var serviceRequestJson = JsonSerializer.Serialize(serviceRequest);

            var request = new OrchestratorRequest
            {
                Service = _config.AiPlugin,
                ServiceRequest = serviceRequestJson,
                AgentId = _config.AgentId
            };

            var tryAgain = false;
            try
            {
                using var scope = ServiceResolver.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                var result = await orchestrator.ProcessRequest(request);

                var aiResponse = result?.GetType().GetProperty("Result");
                var response = aiResponse?.GetValue(result) as string;

                if (!string.IsNullOrWhiteSpace(response))
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                }
            }
            catch (Exception e)
            {
                if (e.Message.ToLower()
                    .Contains("an assistant message with 'tool_calls' must be followed by tool messages"))
                {
                    var cachedMessages = await MessageCache.GetCachedMessages(conversationId);
                    if (cachedMessages.Any())
                    {
                        var purgedMessages = RemoveNonUserMessagesFromEnd(cachedMessages);
                        await MessageCache.SaveCachedMessages(conversationId, purgedMessages);
                        await _logger(LogLevel.Error,
                            $"Message cache polluted with toolcall error. Resetting message cache for Teams conversation {conversationId}");
                        tryAgain = true;
                    }
                }
                else
                {
                    await _logger(LogLevel.Error, "An error occurred while processing request for Teams message", e);
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text($"An error occurred while processing request. Error: {e.Message}"),
                        cancellationToken);
                }
            }

            if (tryAgain)
            {
                try
                {
                    using var scope = ServiceResolver.CreateScope();
                    var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                    var result = await orchestrator.ProcessRequest(request);

                    var aiResponse = result?.GetType().GetProperty("Result");
                    var response = aiResponse?.GetValue(result) as string;

                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
                    }
                }
                catch (Exception retryEx)
                {
                    await _logger(LogLevel.Error, "Retry after cache purge also failed", retryEx);
                    await turnContext.SendActivityAsync(
                        MessageFactory.Text($"An error occurred while processing request. Error: {retryEx.Message}"),
                        cancellationToken);
                }
            }
        }
        catch (Exception e)
        {
            await _logger(LogLevel.Error, $"Unhandled error in Teams message handler: {e.Message}", e);
        }
    }

    /// <summary>
    /// WP6B-8 review follow-up: TeamsAttachmentValidator only validated the literal contentUrl
    /// before the fetch; a bare HttpClient with its default AllowAutoRedirect=true would then
    /// follow a 302 from an allowed public host straight to an internal/loopback/cloud-metadata
    /// address, bypassing the check entirely (TOCTOU via redirect). This disables automatic
    /// redirect-following and re-validates every hop's target with
    /// <see cref="TeamsAttachmentValidator.IsRedirectAllowed"/> before following it.
    /// </summary>
    private static async Task<string> DownloadAttachmentSafelyAsync(string contentUrl, int maxRedirects = 5)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var httpClient = new HttpClient(handler);

        var currentUri = new Uri(contentUrl, UriKind.Absolute);

        for (var hop = 0; ; hop++)
        {
            using var response = await httpClient.GetAsync(currentUri);

            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
            {
                if (hop >= maxRedirects)
                    throw new InvalidOperationException("Too many redirects while downloading Teams attachment.");

                if (!TeamsAttachmentValidator.IsRedirectAllowed(
                        currentUri, response.Headers.Location.ToString(), out currentUri, out var reason))
                {
                    throw new InvalidOperationException($"Refusing to follow Teams attachment redirect: {reason}");
                }

                continue;
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return Convert.ToBase64String(bytes);
        }
    }

    protected override async Task<InvokeResponse> OnTeamsCardActionInvokeAsync(ITurnContext<IInvokeActivity> turnContext, CancellationToken cancellationToken)
    {
        try
        {
            // Handle Adaptive Card Action.Submit payloads
            var value = turnContext.Activity.Value as System.Text.Json.JsonElement?;
            if (value is null) return new InvokeResponse { Status = 200 };

            var actionData = value.Value;
            if (!actionData.TryGetProperty("action", out var actionProp)) return new InvokeResponse { Status = 200 };

            var action = actionProp.GetString();
            if (action != "hq_confirmation_action") return new InvokeResponse { Status = 200 };

            // WP6B-4: same channel-scoping as the text-based path above — a card action on a
            // different channel/team can never match this channel's pending confirmation.
            var channelKey = turnContext.Activity.TeamsGetChannelId();
            if (!_pendingConfirmations.TryGet(channelKey, out var pendingConfirmation) ||
                !_notificationService.DoesConfirmationExist(pendingConfirmation.Id ?? Guid.Empty, out _))
            {
                return new InvokeResponse { Status = 200 };
            }

            if (!actionData.TryGetProperty("optionKey", out var optionKeyProp)) return new InvokeResponse { Status = 200 };
            var optionKey = optionKeyProp.GetString();

            if (pendingConfirmation.Options.Any(a =>
                    string.Equals(a.Key, optionKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                var optionValue = pendingConfirmation.Options
                    .First(a => string.Equals(a.Key, optionKey, StringComparison.InvariantCultureIgnoreCase))
                    .Value;
                var confirmationResult = await _confirm(pendingConfirmation.Id.ToString(), optionValue);

                // Send result as a follow-up message
                await SendConfirmationResult(turnContext, confirmationResult, cancellationToken);
                _pendingConfirmations.Remove(channelKey);
            }
        }
        catch (Exception e)
        {
            await _logger(LogLevel.Error, $"Error handling Teams card action: {e.Message}", e);
        }

        return new InvokeResponse { Status = 200 };
    }

    private async Task<bool> ProcessSpecialCommands(ITurnContext turnContext, string conversationId, string message, CancellationToken cancellationToken)
    {
        if (message.StartsWith('/'))
        {
            string response = null;
            switch (message.ToLower())
            {
                case "/reset":
                    await MessageCache.ClearMessageCache(conversationId);
                    response = "Message cache reset";
                    break;
            }

            if (!string.IsNullOrWhiteSpace(response))
            {
                await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
            }

            return true;
        }

        return false;
    }

    private async Task SendConfirmationResult(ITurnContext turnContext, object confirmationResult, CancellationToken cancellationToken)
    {
        if (confirmationResult.GetType().GetProperty("Success")?.GetValue(confirmationResult) is bool success)
        {
            if (success)
            {
                var result = confirmationResult.GetType().GetProperty("Result")?.GetValue(confirmationResult);
                var response = "Command successfully run";
                if (result is not null)
                {
                    response = result is string resultString
                        ? resultString
                        : JsonSerializer.Serialize(result);
                }

                await turnContext.SendActivityAsync(MessageFactory.Text(response), cancellationToken);
            }
            else
            {
                var error = confirmationResult.GetType().GetProperty("Error")?.GetValue(confirmationResult);
                if (error is string errorString)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(errorString), cancellationToken);
                }
            }
        }
    }

    private List<ChatMessageHistory> RemoveNonUserMessagesFromEnd(List<ChatMessageHistory> cachedMessages)
    {
        var modifiedMessages = new List<ChatMessageHistory>(cachedMessages);

        for (int i = modifiedMessages.Count - 1; i >= 0; i--)
        {
            if (modifiedMessages[i].Role == "user")
                break;

            modifiedMessages.RemoveAt(i);
        }

        return modifiedMessages;
    }
}
