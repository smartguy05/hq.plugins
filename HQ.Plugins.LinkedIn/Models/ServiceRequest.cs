using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Chat parameters
    public string ChatId { get; set; }
    public string Before { get; set; }
    public string After { get; set; }
    public string Cursor { get; set; }
    public int? Limit { get; set; }

    // Profile parameters
    public string Username { get; set; }
    public bool? NotifyProfile { get; set; }

    // Post parameters
    public string Caption { get; set; }
    public string Attachments { get; set; }

    // Comment parameters
    public string PostId { get; set; }
    public string Text { get; set; }

    // Invitation parameters
    public string InvitationMessage { get; set; }
    public string ConversationId { get; set; }

    // New chat parameters
    public string AccountType { get; set; }
    public string Title { get; set; }
}
