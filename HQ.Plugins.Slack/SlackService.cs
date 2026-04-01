using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Chat;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Slack.Models;
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
    Func<string, bool, ValueTask<object>> confirm,
    string botUserId = null)
    : IEventHandler<MessageEvent>, IBlockActionHandler<ButtonAction>, IDisposable
{
    public Confirmation PendingConfirmation;
    public const string ConfirmationActionId = "hq_confirmation_action";
    private string _activeChannelId;
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
        // Skip subtypes (edits, joins, etc.) but allow file_share and bot_message
        if (slackEvent.Subtype != null && slackEvent.Subtype != "file_share" && slackEvent.Subtype != "bot_message")
            return;

        // Skip bot messages unless this bot is explicitly @mentioned
        if (slackEvent.BotId != null)
        {
            var mentionTag = !string.IsNullOrEmpty(botUserId) ? $"<@{botUserId}>" : null;
            if (mentionTag == null || slackEvent.Text == null || !slackEvent.Text.Contains(mentionTag))
                return;
        }

        try
        {
            _activeChannelId ??= slackEvent.Channel;

            var isDm = slackEvent.Channel.StartsWith("D");

            // For DMs, use user ID as the reply target — Slack's chat.postMessage accepts
            // user IDs and auto-opens/finds the DM conversation. This avoids channel_not_found
            // errors when the bot token lacks im:read/im:write scopes.
            var replyTo = isDm ? slackEvent.User : slackEvent.Channel;

            // In channels (not DMs), only respond when @mentioned
            if (!isDm && !string.IsNullOrEmpty(botUserId))
            {
                var mentionTag = $"<@{botUserId}>";
                if (slackEvent.Text == null || !slackEvent.Text.Contains(mentionTag))
                    return;
            }

            var agentPrefix = config.AgentId.HasValue ? $"slack-{config.AgentId.Value}" : "slack";
            var conversationId = isDm
                ? $"{agentPrefix}-{slackEvent.User}"
                : $"{agentPrefix}-{slackEvent.Channel}";

            var messageText = slackEvent.Text ?? string.Empty;

            // Strip bot mention from message text so it doesn't confuse the LLM
            if (!isDm && !string.IsNullOrEmpty(botUserId))
            {
                messageText = messageText.Replace($"<@{botUserId}>", "").Trim();
            }

            // Resolve sender name for context
            string senderName = null;
            if (!isDm)
            {
                try
                {
                    // For bot messages, resolve via bot info first (User field may still be set on bot messages)
                    if (!string.IsNullOrEmpty(slackEvent.BotId))
                    {
                        var botInfo = await client.Bots.Info(slackEvent.BotId);
                        senderName = botInfo?.Name;
                    }

                    // Fall back to user info if bot info didn't yield a name
                    if (string.IsNullOrEmpty(senderName) && !string.IsNullOrEmpty(slackEvent.User))
                    {
                        var userInfo = await client.Users.Info(slackEvent.User);
                        // Prefer non-empty values: Name is the Slack handle, most reliable for @mentions
                        senderName = !string.IsNullOrWhiteSpace(userInfo.Profile?.DisplayName)
                            ? userInfo.Profile.DisplayName
                            : !string.IsNullOrWhiteSpace(userInfo.RealName)
                                ? userInfo.RealName
                                : userInfo.Name;
                    }
                }
                catch (Exception e)
                {
                    await logger(LogLevel.Warning, $"Failed to resolve sender name: {e.Message}");
                }
            }

            await logger(LogLevel.Info, $"Slack received message in {slackEvent.Channel}: '{messageText}'");

            if (await ProcessSpecialCommands(replyTo, conversationId, messageText))
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
                    await SendConfirmationResult(confirmationResult, replyTo);
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

            // In channels, prefix the message with sender info so the LLM knows who to @mention
            var promptText = messageText;
            if (!isDm && !string.IsNullOrEmpty(senderName))
            {
                if (string.IsNullOrWhiteSpace(messageText))
                    promptText = $"[You were mentioned by @{senderName} in this channel]";
                else
                    promptText = $"[Message from @{senderName}]: {messageText}";
            }
            else if (string.IsNullOrWhiteSpace(messageText))
            {
                promptText = "[You were mentioned in this channel]";
            }

            var serviceRequest = new
            {
                SystemPrompt = (string)null,
                UserPrompt = promptText,
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
            var hadError = false;
            try
            {
                using var scope = ServiceResolver.CreateScope();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IOrchestrator>();
                var result = await orchestrator.ProcessRequest(request);

                var aiResponse = result?.GetType().GetProperty("Result");
                var response = aiResponse?.GetValue(result) as string;

                if (!string.IsNullOrWhiteSpace(response))
                {
                    await SendMessage(response, replyTo);
                }
            }
            catch (Exception e)
            {
                var errorLower = e.Message.ToLower();
                if (errorLower.Contains("an assistant message with 'tool_calls' must be followed by tool messages")
                    || errorLower.Contains("tool_use") && errorLower.Contains("without") && errorLower.Contains("tool_result"))
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
                    hadError = true;
                    await logger(LogLevel.Error, "An error occurred while processing request for Slack message", e);
                    await SendMessage($"An error occurred while processing request. Error: {e.Message}",
                        replyTo);
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
                        await SendMessage(response, replyTo);
                    }
                }
                catch (Exception retryEx)
                {
                    hadError = true;
                    await logger(LogLevel.Error, "Retry after cache purge also failed", retryEx);
                    await SendMessage($"An error occurred while processing request. Error: {retryEx.Message}",
                        replyTo);
                }
            }

            // Replace thinking indicator — swap to warning on error, otherwise just remove
            try { await client.Reactions.RemoveFromMessage("thinking_face", slackEvent.Channel, slackEvent.Ts); }
            catch { /* best-effort — may already be removed or message deleted */ }

            if (hadError)
            {
                try { await client.Reactions.AddToMessage("warning", slackEvent.Channel, slackEvent.Ts); }
                catch { /* best-effort */ }
            }
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

        // Resolve display names / @mentions to actual Slack IDs
        channelId = await ResolveTarget(channelId);

        // Resolve @mentions in the message text to Slack <@ID> format
        messageText = await ResolveMentionsInText(messageText);

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

        // Resolve channel name to ID if needed (Slack API requires channel IDs like C01ABCDEF23)
        if (!channelId.StartsWith("C") && !channelId.StartsWith("G") && !channelId.StartsWith("D"))
        {
            channelId = await ResolveChannelId(channelId) ?? channelId;
        }

        var confirmationText = BuildConfirmationText(confirmation);

        var options = confirmation.Options is { Count: > 0 }
            ? confirmation.Options
            : new Dictionary<string, bool> { { "Yes", true }, { "No", false } };

        var buttons = options.Select((option, index) => (IActionElement)new SlackNet.Blocks.Button
        {
            Text = new PlainText(option.Key),
            ActionId = $"{ConfirmationActionId}_{index}",
            Value = option.Key,
            Style = option.Value ? ButtonStyle.Primary : ButtonStyle.Danger
        }).ToList();

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

    /// <summary>
    /// Finds @mentions in message text (e.g. @username, @First Last) that aren't already
    /// in Slack format and resolves them to &lt;@USER_ID&gt; format for clickable mentions.
    /// </summary>
    private async Task<string> ResolveMentionsInText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        // Match @word (and optionally a second word for "First Last") not already in <@...>
        var mentionPattern = new Regex(@"(?<!\<)@([\w.-]+(?:\s[\w.-]+)?)");
        var matches = mentionPattern.Matches(text);
        if (matches.Count == 0)
            return text;

        // Fetch users once for all matches
        List<SlackNet.User> users;
        try
        {
            var result = await client.Users.List();
            users = result.Members.Where(u => !u.Deleted).ToList();
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Warning, $"Failed to fetch users for mention resolution: {ex.Message}");
            return text;
        }

        // Fetch channels once for all matches
        List<SlackNet.Conversation> channels;
        try
        {
            var channelResult = await client.Conversations.List(
                excludeArchived: true,
                types: new[] { ConversationType.PublicChannel, ConversationType.PrivateChannel });
            channels = channelResult.Channels.ToList();
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Warning, $"Failed to fetch channels for mention resolution: {ex.Message}");
            channels = new List<SlackNet.Conversation>();
        }

        // Process longest matches first to avoid partial replacements
        var orderedMatches = matches.Cast<Match>()
            .OrderByDescending(m => m.Groups[1].Value.Length)
            .ToList();

        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in orderedMatches)
        {
            var name = match.Groups[1].Value;
            if (resolved.Contains(name))
                continue;

            // Try channel match first (LLMs often use @channel-name instead of #channel-name)
            var channel = channels.FirstOrDefault(c =>
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                text = Regex.Replace(text, $@"(?<!\<)@{Regex.Escape(name)}\b", $"<#{channel.Id}|{channel.Name}>");
                resolved.Add(name);
                continue;
            }

            var user = FindUserByName(users, name);

            // If "First Last" didn't match, try just the first word
            if (user == null && name.Contains(' '))
            {
                var firstName = name.Split(' ')[0];
                if (!resolved.Contains(firstName))
                {
                    user = FindUserByName(users, firstName);
                    if (user != null)
                        name = firstName; // only replace the first name portion
                }
            }

            if (user != null)
            {
                text = Regex.Replace(text, $@"(?<!\<)@{Regex.Escape(name)}\b", $"<@{user.Id}>");
                resolved.Add(name);
            }
        }

        return text;
    }

    private static SlackNet.User FindUserByName(List<SlackNet.User> users, string name)
    {
        bool Matches(string field) =>
            !string.IsNullOrEmpty(field) &&
            string.Equals(field, name, StringComparison.OrdinalIgnoreCase);

        bool MatchesNoSpaces(string field) =>
            !string.IsNullOrEmpty(field) &&
            string.Equals(field.Replace(" ", ""), name, StringComparison.OrdinalIgnoreCase);

        return users.FirstOrDefault(u =>
            Matches(u.Name) ||
            Matches(u.Profile?.DisplayName) ||
            Matches(u.Profile?.RealNameNormalized) ||
            Matches(u.RealName) ||
            MatchesNoSpaces(u.Profile?.RealNameNormalized) ||
            MatchesNoSpaces(u.RealName) ||
            // First name match as fallback
            (u.Profile?.RealNameNormalized?.Split(' ').FirstOrDefault() is { } first &&
             string.Equals(first, name, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Resolves a target that might be a display name (@user, #channel, or plain name)
    /// to an actual Slack ID. Returns the input unchanged if it's already an ID.
    /// </summary>
    private async Task<string> ResolveTarget(string target)
    {
        // Already a Slack ID (C=channel, G=group, D=DM, U=user, W=enterprise user)
        if (target.Length > 1 && "CGDUW".Contains(target[0]) && target.All(c => char.IsLetterOrDigit(c)))
            return target;

        var name = target.TrimStart('@', '#');

        // Try matching a user by name, display name, or real name
        try
        {
            var users = await client.Users.List();
            var userMatch = users.Members.FirstOrDefault(u =>
                !u.IsBot && !u.Deleted &&
                (string.Equals(u.Name, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(u.Profile?.DisplayName, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(u.Profile?.RealNameNormalized, name, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(u.RealName, name, StringComparison.OrdinalIgnoreCase)));

            if (userMatch != null)
            {
                await logger(LogLevel.Info, $"Resolved Slack target '{target}' to user ID '{userMatch.Id}'");
                return userMatch.Id;
            }
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Warning, $"Failed to search users for '{target}': {ex.Message}");
        }

        // Try matching a channel by name
        var channelId = await ResolveChannelId(name);
        if (channelId != null)
            return channelId;

        await logger(LogLevel.Warning, $"Could not resolve Slack target '{target}' — passing through as-is");
        return target;
    }

    private async Task<string> ResolveChannelId(string channelName)
    {
        try
        {
            var result = await client.Conversations.List(
                excludeArchived: true,
                types: new[] { ConversationType.PublicChannel, ConversationType.PrivateChannel });
            var match = result.Channels.FirstOrDefault(c =>
                string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                await logger(LogLevel.Info, $"Resolved Slack channel name '{channelName}' to ID '{match.Id}'");
                return match.Id;
            }
            await logger(LogLevel.Warning, $"Could not find Slack channel with name '{channelName}'");
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Failed to resolve Slack channel name '{channelName}': {ex.Message}", ex);
        }
        return null;
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

    public async Task<object> OpenConversation(string userIds)
    {
        try
        {
            var ids = userIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (ids.Count == 0)
                return new { Success = false, Error = "At least one user ID is required" };

            var channelId = await client.Conversations.Open(ids);
            return new
            {
                Success = true,
                ChannelId = channelId,
            };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack open conversation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> ListUsers()
    {
        try
        {
            var result = await client.Users.List();
            var users = result.Members
                .Where(u => !u.IsBot && !u.Deleted && u.Id != "USLACKBOT")
                .Select(u => new
                {
                    u.Id,
                    u.Name,
                    RealName = u.RealName,
                    DisplayName = u.Profile?.DisplayName
                }).ToList();

            return new { Success = true, Users = users };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Slack list users failed: {ex.Message}", ex);
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
        var lower = message.ToLower().Trim();

        // Accept both "!command" and "/command" prefixes (Slack intercepts "/" so "!" is the reliable option)
        if (lower.StartsWith('!') || lower.StartsWith('/'))
        {
            var command = lower.TrimStart('!', '/');
            string response = null;
            switch (command)
            {
                case "reset":
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
                var result = confirmationResult.GetType().GetProperty("Result")?.GetValue(confirmationResult)
                         ?? confirmationResult.GetType().GetProperty("Message")?.GetValue(confirmationResult);
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
