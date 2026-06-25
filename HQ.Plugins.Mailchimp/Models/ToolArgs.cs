using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Mailchimp.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Tools that support the confirmation flow implement <see cref="IPluginServiceRequest"/>
/// (the framework envelope fields are <c>[Injected]</c> so they are kept out of the schema yet
/// preserved across the confirmation replay round-trip).
/// </summary>

public class ListAudiencesArgs
{
    [Description("Max results (default 25)")]
    public int? Count { get; set; }
}

public class GetAudienceArgs
{
    [Required, Description("The audience/list ID")]
    public string AudienceId { get; set; }
}

public class AddMemberArgs
{
    [Required]
    public string AudienceId { get; set; }

    [Required]
    public string Email { get; set; }

    [Description("subscribed | pending | unsubscribed (default subscribed)")]
    public string Status { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }
}

public class GetMemberArgs
{
    [Required]
    public string AudienceId { get; set; }

    [Required]
    public string Email { get; set; }
}

public class UpdateMemberArgs
{
    [Required]
    public string AudienceId { get; set; }

    [Required]
    public string Email { get; set; }

    [Description("subscribed | unsubscribed | cleaned")]
    public string Status { get; set; }

    public string FirstName { get; set; }

    public string LastName { get; set; }
}

public class AddMemberTagsArgs
{
    [Required]
    public string AudienceId { get; set; }

    [Required]
    public string Email { get; set; }

    [Required, Description("Comma-separated tag names")]
    public string Tags { get; set; }
}

public class ListCampaignsArgs
{
    [Description("Max results (default 25)")]
    public int? Count { get; set; }
}

public class CreateCampaignArgs
{
    [Required]
    public string AudienceId { get; set; }

    [Required, Description("Subject line")]
    public string Subject { get; set; }

    [Required]
    public string FromName { get; set; }

    [Required, Description("Reply-to email")]
    public string ReplyTo { get; set; }

    [Description("Internal campaign title")]
    public string Title { get; set; }
}

public class SetCampaignContentArgs
{
    [Required]
    public string CampaignId { get; set; }

    [Required, Description("Full HTML body of the email")]
    public string HtmlContent { get; set; }
}

public class SendCampaignArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Required]
    public string CampaignId { get; set; }
}
