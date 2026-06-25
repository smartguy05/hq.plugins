using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Mailchimp.Models;

namespace HQ.Plugins.Mailchimp;

/// <summary>Tool surface for Mailchimp email marketing (audiences, members, campaigns).</summary>
public class MailchimpService
{
    private const string PluginName = "Mailchimp";
    private readonly LogDelegate _logger;
    private readonly INotificationService _notificationService;

    public MailchimpService(INotificationService notificationService, LogDelegate logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    private static string[] SplitTags(string tags) =>
        string.IsNullOrWhiteSpace(tags) ? [] : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Display(Name = MailchimpMethods.ListAudiences)]
    [Description("List audiences (mailing lists) in the account.")]
    [Parameters(typeof(ListAudiencesArgs))]
    public Task<object> ListAudiences(ServiceConfig config, ListAudiencesArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var doc = await client.GetAsync($"/lists?count={r.Count ?? 25}");
            return new { Success = true, Audiences = Prop(doc, "lists") };
        });

    [Display(Name = MailchimpMethods.GetAudience)]
    [Description("Get a single audience (list) with member counts and stats.")]
    [Parameters(typeof(GetAudienceArgs))]
    public Task<object> GetAudience(ServiceConfig config, GetAudienceArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var doc = await client.GetAsync($"/lists/{r.AudienceId}");
            return new { Success = true, Audience = doc };
        });

    [Display(Name = MailchimpMethods.AddMember)]
    [Description("Add or update a subscriber in an audience.")]
    [Parameters(typeof(AddMemberArgs))]
    public Task<object> AddMember(ServiceConfig config, AddMemberArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            // PUT upserts by subscriber hash so re-adds don't error.
            var body = new
            {
                email_address = r.Email,
                status_if_new = string.IsNullOrWhiteSpace(r.Status) ? "subscribed" : r.Status,
                status = string.IsNullOrWhiteSpace(r.Status) ? "subscribed" : r.Status,
                merge_fields = new { FNAME = r.FirstName ?? "", LNAME = r.LastName ?? "" }
            };
            var doc = await client.PutAsync($"/lists/{r.AudienceId}/members/{MailchimpClient.SubscriberHash(r.Email)}", body);
            return new { Success = true, Member = doc };
        });

    [Display(Name = MailchimpMethods.GetMember)]
    [Description("Get a subscriber by email address.")]
    [Parameters(typeof(GetMemberArgs))]
    public Task<object> GetMember(ServiceConfig config, GetMemberArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var doc = await client.GetAsync($"/lists/{r.AudienceId}/members/{MailchimpClient.SubscriberHash(r.Email)}");
            return new { Success = true, Member = doc };
        });

    [Display(Name = MailchimpMethods.UpdateMember)]
    [Description("Update a subscriber's status or name.")]
    [Parameters(typeof(UpdateMemberArgs))]
    public Task<object> UpdateMember(ServiceConfig config, UpdateMemberArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var body = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(r.Status)) body["status"] = r.Status;
            if (r.FirstName is not null || r.LastName is not null)
                body["merge_fields"] = new { FNAME = r.FirstName ?? "", LNAME = r.LastName ?? "" };
            if (body.Count == 0) return new { Success = false, Error = "Provide status and/or name to update." };
            var doc = await client.PatchAsync($"/lists/{r.AudienceId}/members/{MailchimpClient.SubscriberHash(r.Email)}", body);
            return new { Success = true, Member = doc };
        });

    [Display(Name = MailchimpMethods.AddMemberTags)]
    [Description("Add tags to a subscriber.")]
    [Parameters(typeof(AddMemberTagsArgs))]
    public Task<object> AddMemberTags(ServiceConfig config, AddMemberTagsArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var tags = SplitTags(r.Tags).Select(t => new { name = t, status = "active" });
            await client.PostAsync($"/lists/{r.AudienceId}/members/{MailchimpClient.SubscriberHash(r.Email)}/tags", new { tags });
            return new { Success = true, r.Email, Tags = SplitTags(r.Tags) };
        });

    [Display(Name = MailchimpMethods.ListCampaigns)]
    [Description("List campaigns.")]
    [Parameters(typeof(ListCampaignsArgs))]
    public Task<object> ListCampaigns(ServiceConfig config, ListCampaignsArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var doc = await client.GetAsync($"/campaigns?count={r.Count ?? 25}");
            return new { Success = true, Campaigns = Prop(doc, "campaigns") };
        });

    [Display(Name = MailchimpMethods.CreateCampaign)]
    [Description("Create a regular email campaign targeting an audience. Returns a campaign ID to set content and send.")]
    [Parameters(typeof(CreateCampaignArgs))]
    public Task<object> CreateCampaign(ServiceConfig config, CreateCampaignArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            var body = new
            {
                type = "regular",
                recipients = new { list_id = r.AudienceId },
                settings = new
                {
                    subject_line = r.Subject,
                    from_name = r.FromName,
                    reply_to = r.ReplyTo,
                    title = string.IsNullOrWhiteSpace(r.Title) ? r.Subject : r.Title
                }
            };
            var doc = await client.PostAsync("/campaigns", body);
            return new { Success = true, CampaignId = Prop(doc, "id"), Campaign = doc };
        });

    [Display(Name = MailchimpMethods.SetCampaignContent)]
    [Description("Set the HTML content of a campaign.")]
    [Parameters(typeof(SetCampaignContentArgs))]
    public Task<object> SetCampaignContent(ServiceConfig config, SetCampaignContentArgs r) =>
        Guard(async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            await client.PutAsync($"/campaigns/{r.CampaignId}/content", new { html = r.HtmlContent });
            return new { Success = true, r.CampaignId };
        });

    [Display(Name = MailchimpMethods.SendCampaign)]
    [Description("Send a campaign to its audience. This emails real subscribers.")]
    [Parameters(typeof(SendCampaignArgs))]
    [SupportsConfirmation]
    public Task<object> SendCampaign(ServiceConfig config, SendCampaignArgs r) =>
        Guard(() => Confirm(config, r, "Send this campaign to its audience?", $"Campaign {r.CampaignId}", async () =>
        {
            using var client = new MailchimpClient(config.ApiKey);
            await client.PostAsync($"/campaigns/{r.CampaignId}/actions/send", new { });
            return new { Success = true, r.CampaignId, Status = "sending" };
        }));

    // ───────────────────────────── Plumbing ─────────────────────────────

    private static object Prop(JsonElement doc, string name) =>
        doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty(name, out var el) ? el : doc;

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
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Mailchimp operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
