using System.Text.Json;
using System.Text.RegularExpressions;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams.Models;

namespace HQ.Plugins.Teams;

public class TeamsService
{
    private readonly TeamsGraphClient _graphClient;
    private readonly LogDelegate _logger;
    private readonly ServiceConfig _config;

    public TeamsService(TeamsGraphClient graphClient, LogDelegate logger, ServiceConfig config)
    {
        _graphClient = graphClient;
        _logger = logger;
        _config = config;
    }

    public async Task<object> SendMessage(string messageText, string teamId, string channelId)
    {
        teamId ??= _config.NotificationTeamId;
        channelId ??= _config.NotificationChannelId;

        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(channelId))
        {
            var errorMessage = "TeamId and ChannelId are required. Configure NotificationTeamId and NotificationChannelId for defaults.";
            await _logger(LogLevel.Error, errorMessage);
            return new { Success = false, Error = errorMessage };
        }

        return await _graphClient.SendChannelMessage(teamId, channelId, messageText);
    }

    public async Task<object> ListTeams()
    {
        return await _graphClient.ListTeams();
    }

    public async Task<object> ListChannels(string teamId)
    {
        if (string.IsNullOrWhiteSpace(teamId))
        {
            return new { Success = false, Error = "TeamId is required" };
        }

        return await _graphClient.ListChannels(teamId);
    }

    public async Task<object> UploadFile(string base64Content, string fileName, string fileType, string teamId, string channelId)
    {
        teamId ??= _config.NotificationTeamId;
        channelId ??= _config.NotificationChannelId;

        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(channelId))
        {
            return new { Success = false, Error = "TeamId and ChannelId are required for file upload" };
        }

        try
        {
            var fileBytes = Convert.FromBase64String(base64Content);
            return await _graphClient.UploadFile(teamId, channelId, fileName, fileBytes);
        }
        catch (FormatException)
        {
            return new { Success = false, Error = "Invalid base64 file content" };
        }
    }

    public async Task<object> DownloadFile(string driveItemId)
    {
        if (string.IsNullOrWhiteSpace(driveItemId))
        {
            return new { Success = false, Error = "DriveItemId is required" };
        }

        return await _graphClient.DownloadFile(driveItemId);
    }

    public async Task<object> SendConfirmationCard(Confirmation confirmation, string teamId, string channelId)
    {
        teamId ??= _config.NotificationTeamId;
        channelId ??= _config.NotificationChannelId;

        if (string.IsNullOrWhiteSpace(teamId) || string.IsNullOrWhiteSpace(channelId))
        {
            return new { Success = false, Error = "No channel available for confirmation message" };
        }

        var confirmationText = BuildConfirmationText(confirmation);

        // Build Adaptive Card with buttons
        var actions = confirmation.Options?.Select(option => new
        {
            type = "Action.Submit",
            title = option.Key,
            style = option.Value ? "positive" : "destructive",
            data = new
            {
                action = "hq_confirmation_action",
                optionKey = option.Key
            }
        }).ToList() ?? [];

        var adaptiveCard = new
        {
            type = "AdaptiveCard",
            version = "1.4",
            body = new object[]
            {
                new
                {
                    type = "TextBlock",
                    text = confirmationText,
                    wrap = true
                }
            },
            actions
        };

        var cardJson = JsonSerializer.Serialize(adaptiveCard);
        return await _graphClient.SendAdaptiveCard(teamId, channelId, cardJson);
    }

    private static string BuildConfirmationText(Confirmation confirmation)
    {
        if (confirmation.Content is null)
        {
            return confirmation.ConfirmationMessage;
        }

        var content = confirmation.Content;
        // Strip HTML tags — Teams handles its own formatting
        content = Regex.Replace(content, @"<[^>]+>", string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(content))
        {
            return $"{confirmation.ConfirmationMessage}\n\n{content}";
        }

        return confirmation.ConfirmationMessage;
    }
}
