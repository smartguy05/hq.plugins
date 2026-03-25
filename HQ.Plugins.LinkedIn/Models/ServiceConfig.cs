using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Sensitive]
    [Tooltip("Relevance AI API key (raw key, not Bearer)")]
    public string RelevanceAiApiKey { get; set; }

    [Tooltip("Relevance AI region slug (e.g. 'us-east') — used to build the base URL")]
    public string RelevanceAiRegion { get; set; }

    [Tooltip("Relevance AI project identifier")]
    public string RelevanceAiProjectId { get; set; }

    [Tooltip("Relevance AI tool ID for get_all_chats")]
    public string GetAllChatsToolId { get; set; }

    [Tooltip("Relevance AI tool ID for get_chat_messages")]
    public string GetChatMessagesToolId { get; set; }

    [Tooltip("Relevance AI tool ID for get_user_profile")]
    public string GetUserProfileToolId { get; set; }

    [Tooltip("Relevance AI tool ID for create_post")]
    public string CreatePostToolId { get; set; }

    [Tooltip("Relevance AI tool ID for send_comment")]
    public string SendCommentToolId { get; set; }

    [Tooltip("Relevance AI tool ID for get_inmail_balance")]
    public string GetInMailBalanceToolId { get; set; }

    [Tooltip("Relevance AI tool ID for send_invitation")]
    public string SendInvitationToolId { get; set; }

    [Tooltip("Relevance AI tool ID for send_message")]
    public string SendMessageToolId { get; set; }

    [Tooltip("Relevance AI tool ID for start_new_chat")]
    public string StartNewChatToolId { get; set; }
}
