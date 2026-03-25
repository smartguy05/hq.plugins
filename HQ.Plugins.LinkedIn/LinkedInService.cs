using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.LinkedIn;

public class LinkedInService
{
    private readonly RelevanceAiClient _client;
    private readonly ServiceConfig _config;

    public LinkedInService(RelevanceAiClient client, ServiceConfig config)
    {
        _client = client;
        _config = config;
    }

    [Display(Name = "get_all_chats")]
    [Description("Retrieves all LinkedIn chat conversations")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> GetAllChats(ServiceConfig config, ServiceRequest request)
    {
        return await _client.TriggerTool(_config.GetAllChatsToolId, new Dictionary<string, object>());
    }

    [Display(Name = "get_chat_messages")]
    [Description("Retrieves messages from a specific LinkedIn chat conversation")]
    [Parameters("""{"type":"object","properties":{"chatId":{"type":"string","description":"The ID of the chat conversation"},"before":{"type":"string","description":"Retrieve messages before this timestamp"},"after":{"type":"string","description":"Retrieve messages after this timestamp"},"cursor":{"type":"string","description":"Pagination cursor"},"limit":{"type":"integer","description":"Maximum number of messages to retrieve"}},"required":["chatId"]}""")]
    public async Task<object> GetChatMessages(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ChatId))
            throw new ArgumentException("chatId is required");

        var parameters = new Dictionary<string, object> { { "chat_id", request.ChatId } };
        if (!string.IsNullOrWhiteSpace(request.Before)) parameters["before"] = request.Before;
        if (!string.IsNullOrWhiteSpace(request.After)) parameters["after"] = request.After;
        if (!string.IsNullOrWhiteSpace(request.Cursor)) parameters["cursor"] = request.Cursor;
        if (request.Limit.HasValue) parameters["limit"] = request.Limit.Value;

        return await _client.TriggerTool(_config.GetChatMessagesToolId, parameters);
    }

    [Display(Name = "get_user_profile")]
    [Description("Retrieves a LinkedIn user's profile information by username")]
    [Parameters("""{"type":"object","properties":{"username":{"type":"string","description":"The LinkedIn username to look up"},"notifyProfile":{"type":"boolean","description":"Whether to notify the profile owner of the view"}},"required":["username"]}""")]
    public async Task<object> GetUserProfile(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new ArgumentException("username is required");

        var parameters = new Dictionary<string, object> { { "username", request.Username } };
        if (request.NotifyProfile.HasValue) parameters["notify_profile"] = request.NotifyProfile.Value;

        return await _client.TriggerTool(_config.GetUserProfileToolId, parameters);
    }

    [Display(Name = "create_post")]
    [Description("Creates a new LinkedIn post with the given caption and optional attachments")]
    [Parameters("""{"type":"object","properties":{"caption":{"type":"string","description":"The text content of the post"},"attachments":{"type":"string","description":"Comma-separated attachment URLs"}},"required":["caption"]}""")]
    public async Task<object> CreatePost(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Caption))
            throw new ArgumentException("caption is required");

        var parameters = new Dictionary<string, object> { { "caption", request.Caption } };
        if (!string.IsNullOrWhiteSpace(request.Attachments)) parameters["attachments"] = request.Attachments;

        return await _client.TriggerTool(_config.CreatePostToolId, parameters);
    }

    [Display(Name = "send_comment")]
    [Description("Sends a comment on a LinkedIn post")]
    [Parameters("""{"type":"object","properties":{"postId":{"type":"string","description":"The ID of the post to comment on"},"text":{"type":"string","description":"The comment text"}},"required":["postId","text"]}""")]
    public async Task<object> SendComment(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PostId))
            throw new ArgumentException("postId is required");
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("text is required");

        var parameters = new Dictionary<string, object>
        {
            { "post_id", request.PostId },
            { "text", request.Text }
        };

        return await _client.TriggerTool(_config.SendCommentToolId, parameters);
    }

    [Display(Name = "get_inmail_balance")]
    [Description("Retrieves the current InMail credit balance")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> GetInMailBalance(ServiceConfig config, ServiceRequest request)
    {
        return await _client.TriggerTool(_config.GetInMailBalanceToolId, new Dictionary<string, object>());
    }

    [Display(Name = "send_invitation")]
    [Description("Sends a LinkedIn connection invitation to a user")]
    [Parameters("""{"type":"object","properties":{"username":{"type":"string","description":"The LinkedIn username to invite"},"invitationMessage":{"type":"string","description":"The message to include with the invitation"},"conversationId":{"type":"string","description":"Optional existing conversation ID"}},"required":["username","invitationMessage"]}""")]
    public async Task<object> SendInvitation(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new ArgumentException("username is required");
        if (string.IsNullOrWhiteSpace(request.InvitationMessage))
            throw new ArgumentException("invitationMessage is required");

        var parameters = new Dictionary<string, object>
        {
            { "username", request.Username },
            { "invitation_message", request.InvitationMessage }
        };
        if (!string.IsNullOrWhiteSpace(request.ConversationId)) parameters["conversation_id"] = request.ConversationId;

        return await _client.TriggerTool(_config.SendInvitationToolId, parameters);
    }

    [Display(Name = "send_message")]
    [Description("Sends a message in an existing LinkedIn chat conversation")]
    [Parameters("""{"type":"object","properties":{"chatId":{"type":"string","description":"The ID of the chat conversation"},"text":{"type":"string","description":"The message text to send"}},"required":["chatId","text"]}""")]
    public async Task<object> SendMessage(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ChatId))
            throw new ArgumentException("chatId is required");
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("text is required");

        var parameters = new Dictionary<string, object>
        {
            { "chat_id", request.ChatId },
            { "text", request.Text }
        };

        return await _client.TriggerTool(_config.SendMessageToolId, parameters);
    }

    [Display(Name = "start_new_chat")]
    [Description("Starts a new LinkedIn chat conversation with a user")]
    [Parameters("""{"type":"object","properties":{"accountType":{"type":"string","description":"The LinkedIn account type (e.g. 'premium', 'basic')"},"username":{"type":"string","description":"The LinkedIn username to chat with"},"text":{"type":"string","description":"The initial message text"},"title":{"type":"string","description":"Optional title for the conversation"}},"required":["accountType","username","text"]}""")]
    public async Task<object> StartNewChat(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.AccountType))
            throw new ArgumentException("accountType is required");
        if (string.IsNullOrWhiteSpace(request.Username))
            throw new ArgumentException("username is required");
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("text is required");

        var parameters = new Dictionary<string, object>
        {
            { "account_type", request.AccountType },
            { "username", request.Username },
            { "text", request.Text }
        };
        if (!string.IsNullOrWhiteSpace(request.Title)) parameters["title"] = request.Title;

        return await _client.TriggerTool(_config.StartNewChatToolId, parameters);
    }
}
