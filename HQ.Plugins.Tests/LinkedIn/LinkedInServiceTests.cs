using System.Net;
using System.Text.Json;
using HQ.Plugins.LinkedIn;
using HQ.Plugins.LinkedIn.Models;
using static HQ.Plugins.Tests.LinkedIn.RelevanceAiClientTests;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInServiceTests : IDisposable
{
    private readonly MockHttpMessageHandler _handler;
    private readonly RelevanceAiClient _client;
    private readonly LinkedInService _service;
    private readonly ServiceConfig _config;

    public LinkedInServiceTests()
    {
        _handler = new MockHttpMessageHandler
        {
            ResponseContent = """{"output": "success"}"""
        };
        var httpClient = new HttpClient(_handler);
        _client = new RelevanceAiClient("key", "us-east", "proj", httpClient);
        _config = new ServiceConfig
        {
            Name = "LinkedIn",
            Description = "Test",
            RelevanceAiApiKey = "key",
            RelevanceAiRegion = "us-east",
            RelevanceAiProjectId = "proj",
            GetAllChatsToolId = "tool-get-all-chats",
            GetChatMessagesToolId = "tool-get-chat-messages",
            GetUserProfileToolId = "tool-get-user-profile",
            CreatePostToolId = "tool-create-post",
            SendCommentToolId = "tool-send-comment",
            GetInMailBalanceToolId = "tool-get-inmail-balance",
            SendInvitationToolId = "tool-send-invitation",
            SendMessageToolId = "tool-send-message",
            StartNewChatToolId = "tool-start-new-chat"
        };
        _service = new LinkedInService(_client, _config);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    #region get_all_chats

    [Fact]
    public async Task GetAllChats_CallsCorrectToolId()
    {
        var request = new ServiceRequest { Method = "get_all_chats" };
        await _service.GetAllChats(_config, request);

        Assert.Contains("tool-get-all-chats", _handler.LastRequest.RequestUri.ToString());
    }

    #endregion

    #region get_chat_messages

    [Fact]
    public async Task GetChatMessages_RequiresChatId()
    {
        var request = new ServiceRequest { Method = "get_chat_messages" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetChatMessages(_config, request));
    }

    [Fact]
    public async Task GetChatMessages_SendsChatIdParam()
    {
        var request = new ServiceRequest { Method = "get_chat_messages", ChatId = "chat-123" };
        await _service.GetChatMessages(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        Assert.Equal("chat-123", body.GetProperty("params").GetProperty("chat_id").GetString());
    }

    [Fact]
    public async Task GetChatMessages_IncludesOptionalParams()
    {
        var request = new ServiceRequest
        {
            Method = "get_chat_messages",
            ChatId = "chat-123",
            Before = "2026-01-01",
            After = "2025-01-01",
            Cursor = "abc",
            Limit = 50
        };
        await _service.GetChatMessages(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        var p = body.GetProperty("params");
        Assert.Equal("2026-01-01", p.GetProperty("before").GetString());
        Assert.Equal("2025-01-01", p.GetProperty("after").GetString());
        Assert.Equal("abc", p.GetProperty("cursor").GetString());
        Assert.Equal(50, p.GetProperty("limit").GetInt32());
    }

    #endregion

    #region get_user_profile

    [Fact]
    public async Task GetUserProfile_RequiresUsername()
    {
        var request = new ServiceRequest { Method = "get_user_profile" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetUserProfile(_config, request));
    }

    [Fact]
    public async Task GetUserProfile_SendsUsernameParam()
    {
        var request = new ServiceRequest { Method = "get_user_profile", Username = "johndoe" };
        await _service.GetUserProfile(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        Assert.Equal("johndoe", body.GetProperty("params").GetProperty("username").GetString());
    }

    #endregion

    #region create_post

    [Fact]
    public async Task CreatePost_RequiresCaption()
    {
        var request = new ServiceRequest { Method = "create_post" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreatePost(_config, request));
    }

    [Fact]
    public async Task CreatePost_SendsCaptionParam()
    {
        var request = new ServiceRequest { Method = "create_post", Caption = "Hello world" };
        await _service.CreatePost(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        Assert.Equal("Hello world", body.GetProperty("params").GetProperty("caption").GetString());
    }

    #endregion

    #region send_comment

    [Fact]
    public async Task SendComment_RequiresPostIdAndText()
    {
        var request = new ServiceRequest { Method = "send_comment", PostId = "post-1" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendComment(_config, request));

        var request2 = new ServiceRequest { Method = "send_comment", Text = "nice" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendComment(_config, request2));
    }

    [Fact]
    public async Task SendComment_SendsParams()
    {
        var request = new ServiceRequest { Method = "send_comment", PostId = "post-1", Text = "great post" };
        await _service.SendComment(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        var p = body.GetProperty("params");
        Assert.Equal("post-1", p.GetProperty("post_id").GetString());
        Assert.Equal("great post", p.GetProperty("text").GetString());
    }

    #endregion

    #region get_inmail_balance

    [Fact]
    public async Task GetInMailBalance_CallsCorrectToolId()
    {
        var request = new ServiceRequest { Method = "get_inmail_balance" };
        await _service.GetInMailBalance(_config, request);

        Assert.Contains("tool-get-inmail-balance", _handler.LastRequest.RequestUri.ToString());
    }

    #endregion

    #region send_invitation

    [Fact]
    public async Task SendInvitation_RequiresUsernameAndMessage()
    {
        var request = new ServiceRequest { Method = "send_invitation", Username = "jane" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendInvitation(_config, request));

        var request2 = new ServiceRequest { Method = "send_invitation", InvitationMessage = "hi" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendInvitation(_config, request2));
    }

    [Fact]
    public async Task SendInvitation_SendsParams()
    {
        var request = new ServiceRequest
        {
            Method = "send_invitation",
            Username = "jane",
            InvitationMessage = "Let's connect"
        };
        await _service.SendInvitation(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        var p = body.GetProperty("params");
        Assert.Equal("jane", p.GetProperty("username").GetString());
        Assert.Equal("Let's connect", p.GetProperty("invitation_message").GetString());
    }

    #endregion

    #region send_message

    [Fact]
    public async Task SendMessage_RequiresChatIdAndText()
    {
        var request = new ServiceRequest { Method = "send_message", ChatId = "chat-1" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendMessage(_config, request));

        var request2 = new ServiceRequest { Method = "send_message", Text = "hello" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendMessage(_config, request2));
    }

    [Fact]
    public async Task SendMessage_SendsParams()
    {
        var request = new ServiceRequest { Method = "send_message", ChatId = "chat-1", Text = "hello" };
        await _service.SendMessage(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        var p = body.GetProperty("params");
        Assert.Equal("chat-1", p.GetProperty("chat_id").GetString());
        Assert.Equal("hello", p.GetProperty("text").GetString());
    }

    #endregion

    #region start_new_chat

    [Fact]
    public async Task StartNewChat_RequiresAccountTypeUsernameAndText()
    {
        var request = new ServiceRequest { Method = "start_new_chat", AccountType = "premium", Username = "joe" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.StartNewChat(_config, request));

        var request2 = new ServiceRequest { Method = "start_new_chat", AccountType = "premium", Text = "hi" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.StartNewChat(_config, request2));

        var request3 = new ServiceRequest { Method = "start_new_chat", Username = "joe", Text = "hi" };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.StartNewChat(_config, request3));
    }

    [Fact]
    public async Task StartNewChat_SendsParams()
    {
        var request = new ServiceRequest
        {
            Method = "start_new_chat",
            AccountType = "premium",
            Username = "joe",
            Text = "Hey there",
            Title = "Intro"
        };
        await _service.StartNewChat(_config, request);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        var p = body.GetProperty("params");
        Assert.Equal("premium", p.GetProperty("account_type").GetString());
        Assert.Equal("joe", p.GetProperty("username").GetString());
        Assert.Equal("Hey there", p.GetProperty("text").GetString());
        Assert.Equal("Intro", p.GetProperty("title").GetString());
    }

    #endregion

    #region ServiceRequest model

    [Fact]
    public void ServiceRequest_DefaultsToNull()
    {
        var request = new ServiceRequest();
        Assert.Null(request.ChatId);
        Assert.Null(request.Username);
        Assert.Null(request.Caption);
        Assert.Null(request.PostId);
        Assert.Null(request.Text);
        Assert.Null(request.InvitationMessage);
        Assert.Null(request.AccountType);
        Assert.Null(request.Title);
        Assert.Null(request.Limit);
    }

    #endregion

    #region Annotation tests

    [Fact]
    public void AllToolMethods_HaveRequiredAnnotations()
    {
        var methods = typeof(LinkedInService).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(HQ.Models.Interfaces.IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(HQ.Models.Interfaces.IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType))
            .ToList();

        Assert.Equal(9, methods.Count);

        foreach (var method in methods)
        {
            var display = method.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false);
            var description = method.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
            var parameters = method.GetCustomAttributes(typeof(HQ.Models.Helpers.ParametersAttribute), false);

            Assert.NotEmpty(display);
            Assert.NotEmpty(description);
            Assert.NotEmpty(parameters);
        }
    }

    #endregion
}
