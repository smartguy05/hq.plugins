using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Post/share
    public string Content { get; set; }
    public string MediaUrl { get; set; }
    public string Visibility { get; set; }

    // Profile lookup
    public string LinkedInProfileUrl { get; set; }
    public string CompanyLinkedInUrl { get; set; }

    // People search (Proxycurl)
    public string Keyword { get; set; }
    public string CurrentCompany { get; set; }
    public string CurrentRole { get; set; }
    public string Location { get; set; }
    public string Industry { get; set; }
    public int? MaxResults { get; set; } = 10;

    // Post reference
    public string PostUrn { get; set; }

    // Engagement
    public string ReactionType { get; set; }
    public string CommentText { get; set; }
    public string OriginalPostUrn { get; set; }
}
