using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable). Confirmation tools implement
/// <see cref="IPluginServiceRequest"/> so the request survives the confirmation replay round-trip.
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class GetChatMessagesArgs
{
    [Required, Description("The ID of the chat conversation")]
    public string ChatId { get; set; }

    [Description("Retrieve messages before this timestamp")]
    public string Before { get; set; }

    [Description("Retrieve messages after this timestamp")]
    public string After { get; set; }

    [Description("Pagination cursor")]
    public string Cursor { get; set; }

    [Description("Maximum number of messages to retrieve")]
    public int? Limit { get; set; }
}

public class GetUserProfileArgs
{
    [Required, Description("The LinkedIn username to look up")]
    public string Username { get; set; }

    [Description("Whether to notify the profile owner of the view")]
    public bool? NotifyProfile { get; set; }
}

public class SearchPeopleArgs
{
    [Required, Description("The search keywords")]
    public string Query { get; set; }

    [Description("Maximum number of results")]
    public int? Limit { get; set; }
}

public class LookupPersonArgs
{
    [Required, Description("The LinkedIn username (the slug in linkedin.com/in/{slug})")]
    public string Username { get; set; }
}

public class SearchCompaniesArgs
{
    [Required, Description("The company search keywords")]
    public string Query { get; set; }

    [Description("Maximum number of results")]
    public int? Limit { get; set; }
}

public class LookupCompanyArgs
{
    [Required, Description("The company universal name / slug")]
    public string CompanyId { get; set; }
}

public class CreatePostArgs : IPluginServiceRequest
{
    // Framework envelope fields — supplied by the orchestrator, hidden from the LLM schema,
    // preserved across the confirmation replay.
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The text content of the post")]
    public string Caption { get; set; }

    [Description("Comma-separated attachment URLs")]
    public string Attachments { get; set; }
}

public class SendCommentArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The ID of the post to comment on")]
    public string PostId { get; set; }

    [Required, Description("The comment text")]
    public string Text { get; set; }
}

public class ReactToPostArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The ID (thread URN) of the post to react to")]
    public string PostId { get; set; }

    [Description("Reaction type: LIKE, PRAISE, EMPATHY, INTEREST, APPRECIATION, ENTERTAINMENT. Defaults to LIKE.")]
    public string Text { get; set; }
}

public class SendInvitationArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The LinkedIn username to invite")]
    public string Username { get; set; }

    [Required, Description("The message to include with the invitation")]
    public string InvitationMessage { get; set; }

    [Description("Optional existing conversation ID")]
    public string ConversationId { get; set; }
}

public class SendMessageArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The ID of the chat conversation")]
    public string ChatId { get; set; }

    [Required, Description("The message text to send")]
    public string Text { get; set; }
}

public class StartNewChatArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required, Description("The LinkedIn account type (e.g. 'premium', 'basic')")]
    public string AccountType { get; set; }

    [Required, Description("The LinkedIn username to chat with")]
    public string Username { get; set; }

    [Required, Description("The initial message text")]
    public string Text { get; set; }

    [Description("Optional title for the conversation")]
    public string Title { get; set; }
}
