using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Agent-facing LinkedIn tool surface, driven by an authenticated browser session
/// (<see cref="ILinkedInBrowser"/>) instead of an external orchestration vendor. The model
/// only ever issues these semantic calls — it never sees the password or session cookie.
///
/// Reads call LinkedIn's internal Voyager API (stable-ish, returns rich JSON); the response
/// always includes the raw payload alongside a best-effort summary so a parsing miss never
/// loses data. Writes are confirmation-gated and rate-limited because this runs on a real
/// account. The Voyager <i>write</i> payload shapes are the most drift-prone part and are
/// isolated in small builders below for easy correction against the live web client.
/// </summary>
public class LinkedInService
{
    private const string PluginName = "HQ.Plugins.LinkedIn";

    private readonly ILinkedInBrowser _browser;
    private readonly INotificationService _notificationService;
    private readonly RateLimitGate _rateLimiter;
    private readonly ServiceConfig _config;
    private readonly LogDelegate _log;

    public LinkedInService(
        ILinkedInBrowser browser,
        ServiceConfig config,
        INotificationService notificationService = null,
        RateLimitGate rateLimiter = null,
        LogDelegate log = null)
    {
        _browser = browser;
        _config = config;
        _notificationService = notificationService;
        _rateLimiter = rateLimiter ?? new RateLimitGate();
        _log = log;
    }

    // ===================== Messaging (reads) =====================

    [Display(Name = "get_all_chats")]
    [Description("Retrieves all LinkedIn chat conversations")]
    [Parameters(typeof(EmptyArgs))]
    public Task<object> GetAllChats(ServiceConfig config, EmptyArgs request)
        => Read(() => _browser.VoyagerAsync("GET", Voyager.Conversations), "conversations");

    [Display(Name = "get_chat_messages")]
    [Description("Retrieves messages from a specific LinkedIn chat conversation")]
    [Parameters(typeof(GetChatMessagesArgs))]
    public Task<object> GetChatMessages(ServiceConfig config, GetChatMessagesArgs request)
    {
        Require(request.ChatId, "chatId");
        return Read(() => _browser.VoyagerAsync("GET", Voyager.ConversationEvents(request.ChatId)), "messages");
    }

    // ===================== Profile / search (metered reads) =====================

    [Display(Name = "get_user_profile")]
    [Description("Retrieves a LinkedIn user's profile information by username")]
    [Parameters(typeof(GetUserProfileArgs))]
    public Task<object> GetUserProfile(ServiceConfig config, GetUserProfileArgs request)
    {
        Require(request.Username, "username");
        return Metered(RateLimitCategory.Search, config.MaxSearchesPerDay, async () =>
        {
            var res = await _browser.VoyagerAsync("GET", Voyager.ProfileView(request.Username));
            return Shape(res, "profile", LinkedInParsing.SummarizeProfile(res.Json));
        });
    }

    [Display(Name = "search_people")]
    [Description("Searches LinkedIn for people matching a query (name, title, company, keywords)")]
    [Parameters(typeof(SearchPeopleArgs))]
    public Task<object> SearchPeople(ServiceConfig config, SearchPeopleArgs request)
    {
        Require(request.Query, "query");
        return Metered(RateLimitCategory.Search, config.MaxSearchesPerDay, async () =>
        {
            var res = await _browser.VoyagerAsync("GET", Voyager.Typeahead(request.Query, "PEOPLE"));
            return Shape(res, "people", LinkedInParsing.SummarizeHits(res.Json));
        });
    }

    [Display(Name = "lookup_person")]
    [Description("Looks up a single LinkedIn person's full profile by username")]
    [Parameters(typeof(LookupPersonArgs))]
    public Task<object> LookupPerson(ServiceConfig config, LookupPersonArgs request)
    {
        Require(request.Username, "username");
        return Metered(RateLimitCategory.Search, config.MaxSearchesPerDay, async () =>
        {
            var res = await _browser.VoyagerAsync("GET", Voyager.ProfileView(request.Username));
            return Shape(res, "person", LinkedInParsing.SummarizeProfile(res.Json));
        });
    }

    [Display(Name = "search_companies")]
    [Description("Searches LinkedIn for companies matching a query")]
    [Parameters(typeof(SearchCompaniesArgs))]
    public Task<object> SearchCompanies(ServiceConfig config, SearchCompaniesArgs request)
    {
        Require(request.Query, "query");
        return Metered(RateLimitCategory.Search, config.MaxSearchesPerDay, async () =>
        {
            var res = await _browser.VoyagerAsync("GET", Voyager.Typeahead(request.Query, "COMPANY"));
            return Shape(res, "companies", LinkedInParsing.SummarizeHits(res.Json));
        });
    }

    [Display(Name = "lookup_company")]
    [Description("Looks up a single LinkedIn company by its universal name (the slug in linkedin.com/company/{slug})")]
    [Parameters(typeof(LookupCompanyArgs))]
    public Task<object> LookupCompany(ServiceConfig config, LookupCompanyArgs request)
    {
        Require(request.CompanyId, "companyId");
        return Metered(RateLimitCategory.Search, config.MaxSearchesPerDay, async () =>
        {
            var res = await _browser.VoyagerAsync("GET", Voyager.Company(request.CompanyId));
            return Shape(res, "company", LinkedInParsing.SummarizeCompany(res.Json));
        });
    }

    [Display(Name = "get_inmail_balance")]
    [Description("Retrieves the current InMail credit balance")]
    [Parameters(typeof(EmptyArgs))]
    public Task<object> GetInMailBalance(ServiceConfig config, EmptyArgs request)
        => Read(() => _browser.VoyagerAsync("GET", Voyager.Entitlements), "entitlements");

    // ===================== Posting / engagement (writes) =====================

    [Display(Name = "create_post")]
    [Description("Creates a new LinkedIn post with the given caption and optional attachments")]
    [Parameters(typeof(CreatePostArgs))]
    [SupportsConfirmation]
    public Task<object> CreatePost(ServiceConfig config, CreatePostArgs request)
    {
        Require(request.Caption, "caption");
        return Confirm(config, request, "Publish this LinkedIn post?", request.Caption, () =>
            Write(() => _browser.VoyagerAsync("POST", Voyager.Shares, BuildShareBody(request))));
    }

    [Display(Name = "send_comment")]
    [Description("Sends a comment on a LinkedIn post")]
    [Parameters(typeof(SendCommentArgs))]
    [SupportsConfirmation]
    public Task<object> SendComment(ServiceConfig config, SendCommentArgs request)
    {
        Require(request.PostId, "postId");
        Require(request.Text, "text");
        return Confirm(config, request, "Post this comment?", request.Text, () =>
            Write(() => _browser.VoyagerAsync("POST", Voyager.Comments(request.PostId),
                new { commentV2 = new { text = new { text = request.Text } } })));
    }

    [Display(Name = "react_to_post")]
    [Description("Reacts to a LinkedIn post (e.g. like)")]
    [Parameters(typeof(ReactToPostArgs))]
    [SupportsConfirmation]
    public Task<object> ReactToPost(ServiceConfig config, ReactToPostArgs request)
    {
        Require(request.PostId, "postId");
        var reaction = string.IsNullOrWhiteSpace(request.Text) ? "LIKE" : request.Text.ToUpperInvariant();
        return Confirm(config, request, $"React '{reaction}' to this post?", request.PostId, () =>
            Write(() => _browser.VoyagerAsync("POST", Voyager.Reactions(request.PostId),
                new { reactionType = reaction })));
    }

    // ===================== Connections / outreach (writes) =====================

    [Display(Name = "send_invitation")]
    [Description("Sends a LinkedIn connection invitation to a user")]
    [Parameters(typeof(SendInvitationArgs))]
    [SupportsConfirmation]
    public Task<object> SendInvitation(ServiceConfig config, SendInvitationArgs request)
    {
        Require(request.Username, "username");
        Require(request.InvitationMessage, "invitationMessage");
        return Confirm(config, request, $"Send a connection invitation to '{request.Username}'?", request.InvitationMessage, () =>
            Metered(RateLimitCategory.Invitation, config.MaxInvitationsPerDay, async () =>
            {
                var body = new
                {
                    invitee = new { invitedProfilePublicId = request.Username },
                    message = request.InvitationMessage
                };
                return Shape(await _browser.VoyagerAsync("POST", Voyager.Invitations, body), "invitation");
            }));
    }

    // ===================== Messaging (writes) =====================

    [Display(Name = "send_message")]
    [Description("Sends a message in an existing LinkedIn chat conversation")]
    [Parameters(typeof(SendMessageArgs))]
    [SupportsConfirmation]
    public Task<object> SendMessage(ServiceConfig config, SendMessageArgs request)
    {
        Require(request.ChatId, "chatId");
        Require(request.Text, "text");
        return Confirm(config, request, "Send this LinkedIn message?", request.Text, () =>
            Metered(RateLimitCategory.Message, config.MaxMessagesPerDay, async () =>
            {
                var body = BuildMessageBody(request.Text);
                return Shape(await _browser.VoyagerAsync("POST", Voyager.SendMessage(request.ChatId), body), "message");
            }));
    }

    [Display(Name = "start_new_chat")]
    [Description("Starts a new LinkedIn chat conversation with a user")]
    [Parameters(typeof(StartNewChatArgs))]
    [SupportsConfirmation]
    public Task<object> StartNewChat(ServiceConfig config, StartNewChatArgs request)
    {
        Require(request.AccountType, "accountType");
        Require(request.Username, "username");
        Require(request.Text, "text");
        return Confirm(config, request, $"Start a new chat with '{request.Username}'?", request.Text, () =>
            Metered(RateLimitCategory.Message, config.MaxMessagesPerDay, async () =>
            {
                var body = new
                {
                    recipientPublicId = request.Username,
                    title = request.Title,
                    message = BuildMessageBody(request.Text)
                };
                return Shape(await _browser.VoyagerAsync("POST", Voyager.CreateConversation, body), "conversation");
            }));
    }

    // ===================== Voyager write-body builders (drift-prone — verify vs live client) =====================

    private static object BuildShareBody(CreatePostArgs request)
    {
        var attachments = string.IsNullOrWhiteSpace(request.Attachments)
            ? Array.Empty<string>()
            : request.Attachments.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new
        {
            visibleToConnectionsOnly = false,
            commentaryV2 = new { text = request.Caption },
            attachments
        };
    }

    private static object BuildMessageBody(string text) => new
    {
        eventCreate = new { value = new { messageCreate = new { body = text, attachments = Array.Empty<object>() } } }
    };

    // ===================== Shared plumbing =====================

    /// <summary>Wraps a metered action behind the daily rate-limit gate.</summary>
    private async Task<object> Metered(RateLimitCategory category, int dailyCap, Func<Task<object>> execute)
    {
        if (!_rateLimiter.TryConsume(category, dailyCap))
        {
            _log?.Invoke(LogLevel.Warning, $"LinkedIn daily {category} cap ({dailyCap}) reached.");
            return new { Success = false, Error = $"Daily {category.ToString().ToLowerInvariant()} limit ({dailyCap}) reached. Try again tomorrow." };
        }
        return await Guard(execute);
    }

    /// <summary>Unmetered read → shaped result.</summary>
    private Task<object> Read(Func<Task<VoyagerResponse>> call, string key)
        => Guard(async () => Shape(await call(), key));

    /// <summary>Unmetered write → shaped result.</summary>
    private Task<object> Write(Func<Task<VoyagerResponse>> call)
        => Guard(async () => Shape(await call(), "result"));

    /// <summary>Stripe-style confirmation wrapper: first call requests confirmation, second executes.</summary>
    private async Task<object> Confirm(ServiceConfig config, IPluginServiceRequest request, string message, string content, Func<Task<object>> execute)
    {
        if (config.RequiresConfirmation && _notificationService != null)
        {
            if (string.IsNullOrWhiteSpace(request.ConfirmationId))
            {
                var confirmation = new Confirmation
                {
                    ConfirmationMessage = message,
                    Content = content,
                    Options = new Dictionary<string, bool> { { "Yes", true }, { "No", false } },
                    Id = Guid.NewGuid()
                };
                return await _notificationService.RequestConfirmation(PluginName, confirmation, request);
            }

            if (!_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
                return new { Success = false, Error = "Action was not confirmed." };
        }

        return await execute();
    }

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (ArgumentException ex)
        {
            return new { Success = false, Error = ex.Message };
        }
        catch (Exception ex)
        {
            _log?.Invoke(LogLevel.Error, $"LinkedIn operation failed: {ex.Message}");
            return new { Success = false, Error = ex.Message };
        }
    }

    /// <summary>Uniform tool result: success flag, the raw Voyager payload, and an optional summary.</summary>
    private static object Shape(VoyagerResponse res, string key, object summary = null)
    {
        if (!res.IsSuccess)
            return new { Success = false, Status = res.Status, Error = "LinkedIn request was not successful.", Raw = res.RawBody };

        return new
        {
            Success = true,
            Status = res.Status,
            Key = key,
            Summary = summary,
            Raw = res.Json
        };
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{name} is required");
    }
}
