using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Chat;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Slack.Models;
using HQ.Services;
using HQ.Services.Orchestration;
using SlackNet;
using SlackNet.Blocks;
using SlackNet.Events;
using SlackNet.Interaction;
using SlackNet.SocketMode;
using Microsoft.Extensions.DependencyInjection;
using SlackNet.WebApi;

namespace HQ.Plugins.Slack;

public class SlackService(
    ISlackApiClient client,
    LogDelegate logger,
    ServiceConfig config,
    INotificationService notificationService,
    Func<string, bool, ValueTask<object>> confirm)
    : IEventHandler<MessageEvent>, IBlockActionHandler<ButtonAction>, IDisposable
{
    public static Confirmation PendingConfirmation;
    public const string ConfirmationActionId = "hq_confirmation_action";
    private static string _activeChannelId;
    private static readonly HttpClient HttpClient = new();

    private static readonly HashSet<string> PermanentErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "not_authed", "invalid_auth", "account_inactive", "token_revoked", "missing_scope",
        "not_allowed_token_type", "ekm_access_denied"
    };
    private const int MaxRetries = 5;

    public async Task Connect(ISlackSocketModeClient socketClient, int attempt = 0)
    {
        await logger(LogLevel.Info, $"Slack Socket Mode connecting (attempt {attempt})...");
        try
        {
            await socketClient.Connect();
            await logger(LogLevel.Info, "Slack Socket Mode connected successfully");
        }
        catch (SlackException se) when (PermanentErrors.Contains(se.ErrorCode))
        {
            await logger(LogLevel.Error,
                $"Slack connection failed with permanent error: {se.ErrorCode}. " +
                "Please check your Slack plugin configuration (BotToken / AppLevelToken) and re-initialize.", se);
            throw;
        }
        catch (Exception e)
        {
            attempt++;
            if (attempt >= MaxRetries)
            {
                await logger(LogLevel.Error,
                    $"Slack Socket Mode connection failed after {MaxRetries} attempts. Giving up.", e);
                throw;
            }

            var delay = Math.Min(5000 * attempt, 30000);
            await logger(LogLevel.Error,
                $"Slack Socket Mode connection failed (attempt {attempt}/{MaxRetries}), retrying in {delay / 1000}s", e);
            await Task.Delay(delay);
            await Connect(socketClient, attempt);
        }
    }

    public async Task Handle(MessageEvent slackEvent)
    {
        // Skip bot messages and subtypes (edits, joins, etc.)
        if (slackEvent.BotId != null || (slackEvent.Subtype != null && slackEvent.Subtype != "file_share"))
            return;

        try
        {
            _activeChannelId ??= slackEvent.Channel;

            var conversationId = slackEvent.Channel.StartsWith("D")
                ? $"slack-{slackEvent.User}"
                : $"slack-{slackEvent.Channel}";

            var messageText = slackEvent.Text ?? string.Empty;

            await logger(LogLevel.Info, $"Slack received message in {slackEvent.Channel}: '{messageText}'");

            if (await ProcessSpecialCommands(slackEvent.Channel, conversationId, messageText))
                return;

            if (PendingConfirmation is not null &&
                notificationService.DoesConfirmationExist(PendingConfirmation.Id ?? Guid.Empty, out _))
            {
                var lowerMessage = messageText.ToLowerInvariant();
                if (PendingConfirmation.Options.Any(a =>
                        string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase)))
                {
                    var value = PendingConfirmation.Options
                        .First(a => string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase))
                        .Value;
                    var confirmationResult = await confirm(PendingConfirmation.Id.ToString(), value);
                    await SendConfirmationResult(confirmationResult, slackEvent.Channel);
                    PendingConfirmation = null;
                    return;
                }

                PendingConfirmation = null;
            }

            // Download attachments if present
            string fileBase64 = null;
            if (slackEvent.Files is { Count: > 0 })
            {
                var lastFile = slackEvent.Files.Last();
                try
                {
                    var downloadResult = await DownloadFile(lastFile.Id);
                    if (downloadResult.GetType().GetProperty("Success")?.GetValue(downloadResult) is true)
                    {
                        fileBase64 = downloadResult.GetType().GetProperty("Content")?.GetValue(downloadResult) as string;
                    }
                }
                catch (Exception e)
                {
                    await logger(LogLevel.Error, $"Failed to download Slack file: {e.Message}", e);
                }
            }

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
                Service = config.AiPlugin,
                ServiceRequest = serviceRequestJson,
                AgentId = config.AgentId
            };

            // Add thinking indicator
            try { await client.Reactions.AddToMessage("thinking_face", slackEvent.Channel, slackEvent.Ts); }
            catch (Exception e) { await logger(LogLevel.Warning, $"Failed to add thinking reaction: {e.Message}"); }

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
                    await SendMessage(response, slackEvent.Channel);
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
                        await logger(LogLevel.Error,
                            $"Message cache polluted with toolcall error. Resetting message cache for Slack channel {slackEvent.Channel}");
                        tryAgain = true;
                    }
                }
                else
                {
                    await logger(LogLevel.Error, "An error occurred while processing request for Slack message", e);
                    await SendMessage($"An error occurred while processing request. Error: {e.Message}",
                        slackEvent.Channel);
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
                        await SendMessage(response, slackEvent.Channel);
                    }
                }
                catch (Exception retryEx)
                {
                    await logger(LogLevel.Error, "Retry after cache purge also failed", retryEx);
                    await SendMessage($"An error occurred while processing request. Error: {retryEx.Message}",
                        slackEvent.Channel);
                }
            }

            // Remove thinking indicator
            try { await client.Reactions.RemoveFromMessage("thinking_face", slackEvent.Channel, slackEvent.Ts); }
            catch { /* best-effort — may already be removed or message deleted */ }
        }
        catch (Exception e)
        {
            await logger(LogLevel.Error, $"Unhandled error in Slack message handler: {e.Message}", e);
        }
    }

    public async Task Handle(ButtonAction action, BlockActionRequest request)
    {
        try
        {
            if (PendingConfirmation is null ||
                !notificationService.DoesConfirmationExist(PendingConfirmation.Id ?? Guid.Empty, out _))
            {
                return;
            }

            var optionKey = action.Value;
            if (PendingConfirmation.Options.Any(a =>
                    string.Equals(a.Key, optionKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                var value = PendingConfirmation.Options
                    .First(a => string.Equals(a.Key, optionKey, StringComparison.InvariantCultureIgnoreCase))
                    .Value;
                var confirmationResult = await confirm(PendingConfirmation.Id.ToString(), value);

                // Update original message to remove buttons (prevent double-click)
                try
                {
                    var confirmationText = BuildConfirmationText(PendingConfirmation);
                    await client.Chat.Update(new MessageUpdate
                    {
                        ChannelId = request.Channel.Id,
                        Ts = request.Message.Ts,
                        Text = $"{confirmationText}\n\n_Selection: {optionKey}_",
                        Blocks = new List<Block>
                        {
                            new SectionBlock
                            {
                                Text = new Markdown($"{confirmationText}\n\n_Selection: {optionKey}_")
                            }
                        }
                    });
                }
                catch (Exception e)
                {
                    await logger(LogLevel.Error, $"Failed to update confirmation message: {e.Message}", e);
                }

                await SendConfirmationResult(confirmationResult, request.Channel.Id);
                PendingConfirmation = null;
            }
        }
        catch (Exception e)
        {
            await logger(LogLevel.Error, $"Error handling Slack button action: {e.Message}", e);
        }
    }

    public async Task<object> SendMessage(string messageText, string channelId = null)
    {
        channelId ??= _activeChannelId;
        if (channelId is null)
        {
            var errorMessage = "A Slack message must first be received to learn the channel ID";
            await logger(LogLevel.Error, errorMessage);
            return new
            {
                Success = false,
                Error = errorMessage
            };
        }

        try
        {
            var response = await client.Chat.PostMessage(new Message
            {
                Channel = channelId,
                Text = messageText
            });

            return new
            {
                Success = true,
                MessageTs = response.Ts,
                Channel = response.Channel
            };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack message send failed: {ex.Message}", ex);
            return new
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<object> SendConfirmationMessage(Confirmation confirmation, string channelId)
    {
        channelId ??= _activeChannelId;
        if (channelId is null)
        {
            return new { Success = false, Error = "No channel ID available for confirmation message" };
        }

        var confirmationText = BuildConfirmationText(confirmation);

        var buttons = confirmation.Options?.Select(option => (IActionElement)new SlackNet.Blocks.Button
        {
            Text = new PlainText(option.Key),
            ActionId = ConfirmationActionId,
            Value = option.Key,
            Style = option.Value ? ButtonStyle.Primary : ButtonStyle.Danger
        }).ToList() ?? [];

        try
        {
            var response = await client.Chat.PostMessage(new Message
            {
                Channel = channelId,
                Text = confirmationText,
                Blocks = new List<Block>
                {
                    new SectionBlock
                    {
                        Text = new Markdown(confirmationText)
                    },
                    new ActionsBlock
                    {
                        Elements = buttons
                    }
                }
            });

            return new
            {
                Success = true,
                MessageTs = response.Ts,
                Channel = response.Channel
            };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack confirmation message send failed: {ex.Message}", ex);
            return new
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<object> UploadFile(string base64Content, string fileName, string mimeType, string channelId)
    {
        channelId ??= _activeChannelId;
        if (channelId is null)
        {
            return new { Success = false, Error = "No channel ID available for file upload" };
        }

        try
        {
            var fileBytes = Convert.FromBase64String(base64Content);
            mimeType ??= "application/octet-stream";

            // Step 1: Get upload URL
            var uploadRequest = new Dictionary<string, string>
            {
                ["filename"] = fileName,
                ["length"] = fileBytes.Length.ToString()
            };

            using var getUrlRequest = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/files.getUploadURLExternal");
            getUrlRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.BotToken);
            getUrlRequest.Content = new FormUrlEncodedContent(uploadRequest);

            var getUrlResponse = await HttpClient.SendAsync(getUrlRequest);
            var getUrlResult = await getUrlResponse.Content.ReadFromJsonAsync<JsonElement>();

            if (!getUrlResult.GetProperty("ok").GetBoolean())
            {
                var error = getUrlResult.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Unknown error";
                return new { Success = false, Error = $"Failed to get upload URL: {error}" };
            }

            var uploadUrl = getUrlResult.GetProperty("upload_url").GetString();
            var fileId = getUrlResult.GetProperty("file_id").GetString();

            // Step 2: Upload file bytes
            using var uploadHttpRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadHttpRequest.Content = new ByteArrayContent(fileBytes);
            uploadHttpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

            await HttpClient.SendAsync(uploadHttpRequest);

            // Step 3: Complete upload and share to channel
            var completePayload = new
            {
                files = new[] { new { id = fileId, title = fileName } },
                channel_id = channelId
            };

            using var completeRequest = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/files.completeUploadExternal");
            completeRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.BotToken);
            completeRequest.Content = JsonContent.Create(completePayload);

            var completeResponse = await HttpClient.SendAsync(completeRequest);
            var completeResult = await completeResponse.Content.ReadFromJsonAsync<JsonElement>();

            if (!completeResult.GetProperty("ok").GetBoolean())
            {
                var error = completeResult.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Unknown error";
                return new { Success = false, Error = $"Failed to complete upload: {error}" };
            }

            return new { Success = true, FileId = fileId, FileName = fileName };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack file upload failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> DownloadFile(string fileId)
    {
        try
        {
            var fileInfo = await client.Files.Info(fileId);
            var file = fileInfo.File;

            if (string.IsNullOrWhiteSpace(file.UrlPrivateDownload) && string.IsNullOrWhiteSpace(file.UrlPrivate))
            {
                return new { Success = false, Error = "File has no downloadable URL" };
            }

            var downloadUrl = file.UrlPrivateDownload ?? file.UrlPrivate;

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.BotToken);

            var response = await HttpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync();
            var base64 = Convert.ToBase64String(bytes);

            return new
            {
                Success = true,
                FileId = file.Id,
                FileName = file.Name,
                MimeType = file.Mimetype,
                Content = base64
            };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack file download failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> ListChannels()
    {
        try
        {
            var result = await client.Conversations.List(
                excludeArchived: true,
                types: new[] { ConversationType.PublicChannel, ConversationType.PrivateChannel });

            var channels = result.Channels.Select(c => new
            {
                c.Id,
                c.Name,
                c.IsPrivate,
                c.NumMembers
            }).ToList();

            return new { Success = true, Channels = channels };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack list channels failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public void Dispose()
    {
    }

    private async Task<bool> ProcessSpecialCommands(string channelId, string conversationId, string message)
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
                await SendMessage(response, channelId);
            }

            return true;
        }

        return false;
    }

    private async Task SendConfirmationResult(object confirmationResult, string channelId)
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

                await SendMessage(response, channelId);
            }
            else
            {
                var error = confirmationResult.GetType().GetProperty("Error")?.GetValue(confirmationResult);
                if (error is string errorString)
                {
                    await SendMessage(errorString, channelId);
                }
            }
        }
    }

    private static string BuildConfirmationText(Confirmation confirmation)
    {
        if (confirmation.Content is null)
        {
            return confirmation.ConfirmationMessage;
        }

        var content = confirmation.Content;
        // Strip HTML tags — Slack handles mrkdwn natively
        content = Regex.Replace(content, @"<[^>]+>", string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(content))
        {
            return $"{confirmation.ConfirmationMessage}\n\n{content}";
        }

        return confirmation.ConfirmationMessage;
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
