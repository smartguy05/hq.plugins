namespace HQ.Plugins.Mailchimp;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on MailchimpService.</summary>
public static class MailchimpMethods
{
    public const string ListAudiences = "list_audiences";
    public const string GetAudience = "get_audience";
    public const string AddMember = "add_member";
    public const string GetMember = "get_member";
    public const string UpdateMember = "update_member";
    public const string AddMemberTags = "add_member_tags";
    public const string ListCampaigns = "list_campaigns";
    public const string CreateCampaign = "create_campaign";
    public const string SetCampaignContent = "set_campaign_content";
    public const string SendCampaign = "send_campaign";
}
