using System.Text.Json;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Chat;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams.Models;
using HQ.Services;
using HQ.Services.Orchestration;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Teams;
using Microsoft.Bot.Schema;

namespace HQ.Plugins.Teams;

public class TeamsBot : TeamsActivityHandler
{
    public static Confirmation PendingConfirmation;
    public static readonly Dictionary<string, ConversationReference> ConversationReferences = new();

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

            // Check for pending confirmations (text-based)
            if (PendingConfirmation is not null &&
                _notificationService.DoesConfirmationExist(PendingConfirmation.Id ?? Guid.Empty, out _))
            {
                var lowerMessage = messageText.ToLowerInvariant();
                if (PendingConfirmation.Options.Any(a =>
                        string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase)))
                {
                    var value = PendingConfirmation.Options
                        .First(a => string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase))
                        .Value;
                    var confirmationResult = await _confirm(PendingConfirmation.Id.ToString(), value);
                    await SendConfirmationResult(turnContext, confirmationResult, cancellationToken);
                    PendingConfirmation = null;
                    return;
                }

                PendingConfirmation = null;
            }

            // Route to orchestrator
            var serviceRequest = new
            {
                SystemPrompt = (string)null,
                UserPrompt = messageText,
                ConversationId = conversationId,
                Photo = (string)null
            };
            var serviceRequestJson = JsonSerializer.Serialize(serviceRequest);

            var request = new OrchestratorRequest
            {
                Service = _config.AiPlugin,
                ServiceRequest = serviceRequestJson
            };

            var tryAgain = false;
            try
            {
                var orchestrator = ServiceResolver.GetOrchestrator();
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
                    var orchestrator = ServiceResolver.GetOrchestrator();
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

            if (PendingConfirmation is null ||
                !_notificationService.DoesConfirmationExist(PendingConfirmation.Id ?? Guid.Empty, out _))
            {
                return new InvokeResponse { Status = 200 };
            }

            if (!actionData.TryGetProperty("optionKey", out var optionKeyProp)) return new InvokeResponse { Status = 200 };
            var optionKey = optionKeyProp.GetString();

            if (PendingConfirmation.Options.Any(a =>
                    string.Equals(a.Key, optionKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                var optionValue = PendingConfirmation.Options
                    .First(a => string.Equals(a.Key, optionKey, StringComparison.InvariantCultureIgnoreCase))
                    .Value;
                var confirmationResult = await _confirm(PendingConfirmation.Id.ToString(), optionValue);

                // Send result as a follow-up message
                await SendConfirmationResult(turnContext, confirmationResult, cancellationToken);
                PendingConfirmation = null;
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
