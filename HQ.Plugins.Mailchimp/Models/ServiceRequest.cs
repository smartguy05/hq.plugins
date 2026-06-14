using HQ.Models.Interfaces;

namespace HQ.Plugins.Mailchimp.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Audience / member
    public string AudienceId { get; set; }    // Mailchimp "list" id
    public string Email { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string Status { get; set; }        // subscribed | unsubscribed | pending | cleaned
    public string Tags { get; set; }          // comma-separated tags to add

    // Campaign
    public string CampaignId { get; set; }
    public string Subject { get; set; }
    public string FromName { get; set; }
    public string ReplyTo { get; set; }
    public string Title { get; set; }
    public string HtmlContent { get; set; }

    // Paging
    public int? Count { get; set; }
}
