using System.Text.Json;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Chat;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Telegram.Models;
using HQ.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace HQ.Plugins.Telegram;

public class TelegramService(TelegramBotClient client, LogDelegate logger, ServiceConfig config, INotificationService notificationService, Func<string,bool,ValueTask<object>> confirm): IDisposable
{
    public static Confirmation PendingConfirmation;
    private static long? _chatId;
    
    public async Task<object> SendMessage(string messageText, string chatId = null, string[] options = null)
    {
        chatId ??= _chatId?.ToString();
        if (chatId is null)
        {
            var errorMessage = "A telegram message must first be received to get the chat Id";
            await logger(LogLevel.Error, errorMessage);
            return new
            {
                Success = false,
                Error = errorMessage
            };
        }
        
        try
        {
            if (Regex.IsMatch(messageText, @".*<html>.+</html>.*", RegexOptions.Singleline))
            {
                var converter = new ReverseMarkdown.Converter();
                messageText = $"<h1></h1> {messageText}";
                messageText = converter.Convert(messageText);
            }
            
            messageText = messageText.Replace("#", "");
            messageText = messageText.Replace(".", "\\.");
            messageText = messageText.Replace("!", "\\!");
            messageText = messageText.Replace("-", "\\-");
            messageText = messageText.Replace("(", "\\(");
            messageText = messageText.Replace(")", "\\)");
            messageText = messageText.Replace("|", "\\|");

            ReplyMarkup responseOptions = Array.Empty<string>();
            if (options != null && options.Any())
            {
                responseOptions = options;
            }
            var message = await client.SendMessage(
                chatId,
                text: messageText,
                parseMode: ParseMode.MarkdownV2,
                replyMarkup: responseOptions
            );

            return new 
            {
                Success = true,
                message.MessageId,
                message.Text,
                SentAt = message.Date
            };
        }
        catch (Exception ex)
        {
            await logger(LogLevel.Error, $"Telegram message send failed: {ex.Message}", ex);
            return new
            {
                Success = false,
                Error = ex.Message
            };
        }
    }

    public async Task<List<string>> GetFileFromMessages(List<Update> updates)
    {
        var photos = new List<string>();
        foreach (var update in updates)
        {
            if (update.Message?.Photo is not null && update.Message.Photo.Any())
            {
                var photo = await client.GetFile(update.Message.Photo.Last().FileId);
                using var stream = new MemoryStream();
                await client.DownloadFile(photo, stream);
                
                var streamBytes = stream.ToArray();
                var base64String = Convert.ToBase64String(streamBytes);
                photos.Add(base64String);
            }
        }
        
        return photos;
    }

    public async Task ListenForMessages(List<Update> updates)
    {
        while (client is not null)
        {
            if (updates.Any())
            {
                var tryAgain = false;
                _chatId ??= updates.FirstOrDefault()?.Message?.Chat.Id;
                
                // only get updates sent in the last 5 minutes
                var filteredUpdates = updates
                    .Where(update => 
                        (DateTime.UtcNow - (update.Message?.Date ?? DateTime.UtcNow)).Minutes <= 5)
                    .ToList();

                if (filteredUpdates.Count != 0)
                {
                    var lastMessage = filteredUpdates.Last();
                    var messages = filteredUpdates.Select(s => s.Message?.Text).ToList();
                    var concatenatedMessage = string.Join($". ", messages);
                    var conversationId = lastMessage.Message?.Chat.Username is not null
                        ? $"telegram-{lastMessage.Message?.Chat.Username}"
                        : null;

                    await logger(LogLevel.Info, $"Telegram bot ${lastMessage.Message?.Chat.Id} received message: '{concatenatedMessage}'");

                    if (await ProcessSpecialCommands(lastMessage.Message?.Chat.Id.ToString(), conversationId, concatenatedMessage))
                    {
                        updates = (await client.GetUpdates(updates.Last().Id + (tryAgain ? 0 : 1)))?.ToList() ?? [];
                        continue;
                    }
                    
                    if (PendingConfirmation is not null && notificationService.DoesConfirmationExist(PendingConfirmation.Id ?? Guid.Empty, out _))
                    {
                        var lowerMessage = concatenatedMessage.ToLowerInvariant();
                        if (PendingConfirmation.Options.Any(a => string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase)))
                        {
                            var value = PendingConfirmation.Options.First(a => string.Equals(a.Key, lowerMessage, StringComparison.InvariantCultureIgnoreCase)).Value;
                            var confirmationResult = await confirm(PendingConfirmation.Id.ToString(), value);
                            if (confirmationResult.GetType().GetProperty("Success")?.GetValue(confirmationResult) is bool success)
                            {
                                if (success)
                                {
                                    var result = confirmationResult.GetType().GetProperty("Result")?.GetValue(confirmationResult);
                                    var response = "Command successfully run";
                                    if (result is not null)
                                    {
                                        if (result is string resultString)
                                        {
                                            response = resultString;
                                        } else if (result is object resultObject)
                                        {
                                            response = JsonSerializer.Serialize(resultObject);
                                        }
                                    }
                                    // send confirmation message
                                    await SendMessage(response, conversationId);
                                }
                                else
                                {
                                    var error = confirmationResult.GetType().GetProperty("Error")?.GetValue(confirmationResult);
                                    if (error is not null && error is string errorString)
                                    {
                                        // send error message
                                        await SendMessage(errorString, conversationId);
                                    }
                                }
                            }
                            
                            PendingConfirmation = null;
                            updates = (await client.GetUpdates(updates.Last().Id + (tryAgain ? 0 : 1)))?.ToList() ?? [];
                            continue;
                        }
                        
                        PendingConfirmation = null;
                    }
                    
                    var images = await GetFileFromMessages(filteredUpdates);
                    
                    var serviceRequest = new
                    {
                        SystemPrompt = (string)null,
                        UserPrompt = concatenatedMessage,
                        ConversationId = conversationId,
                        Photo = images.Any() ? images.Last() : null
                    };
                    var serviceRequestJson = System.Text.Json.JsonSerializer.Serialize(serviceRequest);

                    var request = new OrchestratorRequest
                    {
                        Service = config.AiPlugin,
                        ServiceRequest = serviceRequestJson
                    };

                    try
                    {
                        var orchestrator = ServiceResolver.GetOrchestrator();
                        var result = await orchestrator.ProcessRequest(request);
                
                        var aiResponse = result?.GetType().GetProperty("Result");
                        var response = aiResponse?.GetValue(result) as string;

                        if (!string.IsNullOrWhiteSpace(response))
                        {
                            await SendMessage(response, lastMessage.Message?.Chat.Id.ToString());
                        }
                    }
                    catch (Exception e)
                    {
                        if (e.Message.ToLower()
                            .Contains(
                                "an assistant message with 'tool_calls' must be followed by tool messages"))
                        {
                            var cachedMessages = await MessageCache.GetCachedMessages(conversationId);
                            if (cachedMessages.Any())
                            {
                                var purgedMessages = RemoveNonUserMessagesFromEnd(cachedMessages);
                                await MessageCache.SaveCachedMessages(conversationId, purgedMessages);
                                await logger(LogLevel.Error, $"Message cache polluted with toolcall error. Resetting message cache for id {lastMessage.Message?.Chat.Id}");
                                await logger(LogLevel.Info, $"Original message list: {Environment.NewLine} {cachedMessages}");
                                await logger(LogLevel.Info, $"Purged message list: {Environment.NewLine} {purgedMessages}");
                                tryAgain = true;
                            }
                        }
                        else
                        {
                            await logger(LogLevel.Error, "An error occured while processing request for Telegram message", e);
                            await SendMessage($"An error occurred while processing request. Error: {e.Message}", lastMessage.Message?.Chat.Id.ToString());
                        }
                    }
                }

                var offset = updates.Last().Id + (tryAgain ? 0 : 1);
                updates = (await client.GetUpdates(offset))?.ToList() ?? [];
            }
            else
            {
                updates = (await client.GetUpdates())?.ToList() ?? [];
            }
            
            Thread.Sleep(config.PollingIntervalInMs);
        }
    }
    
    public void Dispose()
    {
    }
    
    private async Task<bool> ProcessSpecialCommands(string chatId, string conversationId, string message)
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
                await SendMessage(response, chatId);
            }
            return true;
        }

        return false;
    }
    
    private List<ChatMessageHistory> RemoveNonUserMessagesFromEnd(List<ChatMessageHistory> cachedMessages)
    {
        var modifiedMessages = new List<ChatMessageHistory>(cachedMessages);

        for (int i = modifiedMessages.Count - 1; i >= 0; i--)
        {
            // If we find a 'user' role message, stop removing
            if (modifiedMessages[i].Role == "user")
                break;

            // Remove messages that are not 'user' role
            modifiedMessages.RemoveAt(i);
        }

        return modifiedMessages;
    }
}