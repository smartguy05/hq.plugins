using Azure.Identity;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams.Models;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace HQ.Plugins.Teams;

public class TeamsGraphClient
{
    private readonly GraphServiceClient _graphClient;
    private readonly LogDelegate _logger;

    public TeamsGraphClient(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        var credential = new ClientSecretCredential(config.TenantId, config.ClientId, config.ClientSecret);
        _graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
    }

    /// <summary>
    /// Test seam — constructs the client without a live Graph connection. Subclasses override the
    /// <c>Fetch*</c> methods to supply canned entities so the projection/wrapping logic can be
    /// exercised without hitting Microsoft Graph. Mirrors <c>WebReaderCommand.SetRenderer</c>.
    /// </summary>
    protected TeamsGraphClient(LogDelegate logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// SAFE-02: wraps author-controlled display text (team/channel names, uploaded file names) in an
    /// <see cref="HQ.Models.Safety.Untrusted{T}"/> envelope so the host can screen it for prompt
    /// injection before it reaches the LLM. IDs/timestamps/statuses are internal and stay raw.
    /// </summary>
    private static object AsUntrusted(string content, string provenance, string source) =>
        string.IsNullOrEmpty(content) ? content : new HQ.Models.Safety.Untrusted<string>(content, provenance, source);

    /// <summary>Fetch seam for <see cref="ListTeams"/> — overridable so tests can bypass Graph.</summary>
    protected virtual async Task<IList<Team>> FetchTeamsAsync()
    {
        var teams = await _graphClient.Teams.GetAsync(requestConfig =>
        {
            requestConfig.QueryParameters.Select = new[] { "id", "displayName", "description" };
        });
        return teams?.Value;
    }

    /// <summary>Fetch seam for <see cref="ListChannels"/> — overridable so tests can bypass Graph.</summary>
    protected virtual async Task<IList<Channel>> FetchChannelsAsync(string teamId)
    {
        var channels = await _graphClient.Teams[teamId].Channels.GetAsync(requestConfig =>
        {
            requestConfig.QueryParameters.Select = new[] { "id", "displayName", "membershipType" };
        });
        return channels?.Value;
    }

    public async Task<object> ListTeams()
    {
        try
        {
            var teams = await FetchTeamsAsync();

            var result = teams?.Select(t => new
            {
                Id = t.Id,
                DisplayName = AsUntrusted(t.DisplayName, "teams-team-name", t.Id),
                Description = AsUntrusted(t.Description, "teams-team-description", t.Id)
            }).ToList();

            return new { Success = true, Teams = result ?? [] };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Teams Graph API ListTeams failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> ListChannels(string teamId)
    {
        try
        {
            var channels = await FetchChannelsAsync(teamId);

            var result = channels?.Select(c => new
            {
                Id = c.Id,
                DisplayName = AsUntrusted(c.DisplayName, "teams-channel-name", c.Id),
                MembershipType = c.MembershipType?.ToString()
            }).ToList();

            return new { Success = true, Channels = result ?? [] };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Teams Graph API ListChannels failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> SendChannelMessage(string teamId, string channelId, string text)
    {
        try
        {
            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    Content = text,
                    ContentType = BodyType.Text
                }
            };

            var response = await _graphClient.Teams[teamId].Channels[channelId].Messages.PostAsync(chatMessage);

            return new { Success = true, MessageId = response?.Id };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Teams Graph API SendChannelMessage failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> UploadFile(string teamId, string channelId, string fileName, byte[] fileBytes)
    {
        try
        {
            // Resolve the channel's files folder (SharePoint drive)
            var filesFolder = await _graphClient.Teams[teamId].Channels[channelId].FilesFolder.GetAsync();
            if (filesFolder?.ParentReference?.DriveId is null)
            {
                return new { Success = false, Error = "Could not resolve channel files folder" };
            }

            var driveId = filesFolder.ParentReference.DriveId;

            // Upload file to the channel's SharePoint folder
            using var stream = new MemoryStream(fileBytes);
            var uploadedItem = await _graphClient.Drives[driveId]
                .Items[filesFolder.Id]
                .ItemWithPath(fileName)
                .Content
                .PutAsync(stream);

            return new
            {
                Success = true,
                DriveItemId = uploadedItem?.Id,
                FileName = uploadedItem?.Name,
                WebUrl = uploadedItem?.WebUrl
            };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Teams Graph API UploadFile failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> DownloadFile(string driveItemId)
    {
        try
        {
            // First get the item metadata to find the drive
            // driveItemId format expected: "driveId/itemId" or just "itemId" with default drive
            string driveId;
            string itemId;

            if (driveItemId.Contains('/'))
            {
                var parts = driveItemId.Split('/', 2);
                driveId = parts[0];
                itemId = parts[1];
            }
            else
            {
                return new { Success = false, Error = "DriveItemId must be in format 'driveId/itemId'" };
            }

            var stream = await _graphClient.Drives[driveId].Items[itemId].Content.GetAsync();
            if (stream is null)
            {
                return new { Success = false, Error = "File content stream was null" };
            }

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var base64 = Convert.ToBase64String(ms.ToArray());

            // Get file metadata
            var item = await _graphClient.Drives[driveId].Items[itemId].GetAsync();

            return new
            {
                Success = true,
                DriveItemId = driveItemId,
                // Uploader-chosen file name is author-controlled — wrap it (SAFE-02). The base64
                // Content is binary and left raw; the host decodes it before any LLM sees text.
                FileName = AsUntrusted(item?.Name, "teams-file-name", driveItemId),
                MimeType = item?.File?.MimeType,
                Content = base64
            };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Teams Graph API DownloadFile failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }

    public async Task<object> SendAdaptiveCard(string teamId, string channelId, string cardJson)
    {
        try
        {
            var chatMessage = new ChatMessage
            {
                Body = new ItemBody
                {
                    ContentType = BodyType.Html,
                    Content = $"<attachment id=\"adaptiveCard\"></attachment>"
                },
                Attachments = new List<ChatMessageAttachment>
                {
                    new()
                    {
                        Id = "adaptiveCard",
                        ContentType = "application/vnd.microsoft.card.adaptive",
                        Content = cardJson
                    }
                }
            };

            var response = await _graphClient.Teams[teamId].Channels[channelId].Messages.PostAsync(chatMessage);

            return new { Success = true, MessageId = response?.Id };
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Teams Graph API SendAdaptiveCard failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
