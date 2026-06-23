using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Zendesk.Models;

namespace HQ.Plugins.Zendesk;

/// <summary>Tool surface for Zendesk Support (tickets, comments, users, macros).</summary>
public class ZendeskService
{
    private readonly LogDelegate _logger;

    public ZendeskService(LogDelegate logger) => _logger = logger;

    private static ZendeskClient Client(ServiceConfig c) => new(c.Subdomain, c.Email, c.ApiToken);

    private static string[] SplitTags(string tags) =>
        string.IsNullOrWhiteSpace(tags) ? null : tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [Display(Name = ZendeskMethods.SearchTickets)]
    [Description("Search tickets using Zendesk search syntax, e.g. \"status:open priority:high\".")]
    [Parameters(typeof(SearchTicketsArgs))]
    public Task<object> SearchTickets(ServiceConfig config, SearchTicketsArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var query = Uri.EscapeDataString($"type:ticket {r.Query}");
            var doc = await client.GetAsync($"/search.json?query={query}&per_page={r.PageSize ?? 25}");
            return new { Success = true, Results = Prop(doc, "results") };
        });

    [Display(Name = ZendeskMethods.GetTicket)]
    [Description("Get a single ticket by ID.")]
    [Parameters(typeof(GetTicketArgs))]
    public Task<object> GetTicket(ServiceConfig config, GetTicketArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/tickets/{r.TicketId}.json");
            return new { Success = true, Ticket = Prop(doc, "ticket") };
        });

    [Display(Name = ZendeskMethods.CreateTicket)]
    [Description("Create a new support ticket.")]
    [Parameters(typeof(CreateTicketArgs))]
    public Task<object> CreateTicket(ServiceConfig config, CreateTicketArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var ticket = new Dictionary<string, object>
            {
                ["subject"] = r.Subject,
                ["comment"] = new { body = r.Comment, @public = r.Public ?? true }
            };
            if (!string.IsNullOrWhiteSpace(r.RequesterId)) ticket["requester_id"] = r.RequesterId;
            if (!string.IsNullOrWhiteSpace(r.AssigneeId)) ticket["assignee_id"] = r.AssigneeId;
            if (!string.IsNullOrWhiteSpace(r.Priority)) ticket["priority"] = r.Priority;
            if (!string.IsNullOrWhiteSpace(r.Status)) ticket["status"] = r.Status;
            if (SplitTags(r.Tags) is { } tags) ticket["tags"] = tags;

            var doc = await client.PostAsync("/tickets.json", new { ticket });
            return new { Success = true, Ticket = Prop(doc, "ticket") };
        });

    [Display(Name = ZendeskMethods.UpdateTicket)]
    [Description("Update a ticket's status, priority, assignee or tags.")]
    [Parameters(typeof(UpdateTicketArgs))]
    public Task<object> UpdateTicket(ServiceConfig config, UpdateTicketArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var ticket = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(r.Status)) ticket["status"] = r.Status;
            if (!string.IsNullOrWhiteSpace(r.Priority)) ticket["priority"] = r.Priority;
            if (!string.IsNullOrWhiteSpace(r.AssigneeId)) ticket["assignee_id"] = r.AssigneeId;
            if (SplitTags(r.Tags) is { } tags) ticket["tags"] = tags;
            if (ticket.Count == 0) return new { Success = false, Error = "Provide at least one field to update." };

            var doc = await client.PutAsync($"/tickets/{r.TicketId}.json", new { ticket });
            return new { Success = true, Ticket = Prop(doc, "ticket") };
        });

    [Display(Name = ZendeskMethods.AddTicketComment)]
    [Description("Add a comment to a ticket. Set public=false for an internal note that the customer cannot see.")]
    [Parameters(typeof(AddTicketCommentArgs))]
    public Task<object> AddTicketComment(ServiceConfig config, AddTicketCommentArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var ticket = new { comment = new { body = r.Comment, @public = r.Public ?? true } };
            var doc = await client.PutAsync($"/tickets/{r.TicketId}.json", new { ticket });
            return new { Success = true, Ticket = Prop(doc, "ticket") };
        });

    [Display(Name = ZendeskMethods.ListTickets)]
    [Description("List recent tickets.")]
    [Parameters(typeof(ListTicketsArgs))]
    public Task<object> ListTickets(ServiceConfig config, ListTicketsArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/tickets.json?per_page={r.PageSize ?? 25}");
            return new { Success = true, Tickets = Prop(doc, "tickets") };
        });

    [Display(Name = ZendeskMethods.GetUser)]
    [Description("Get a Zendesk user (end user or agent) by ID.")]
    [Parameters(typeof(GetUserArgs))]
    public Task<object> GetUser(ServiceConfig config, GetUserArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/users/{r.UserId}.json");
            return new { Success = true, User = Prop(doc, "user") };
        });

    [Display(Name = ZendeskMethods.SearchUsers)]
    [Description("Search users by name, email or other attributes.")]
    [Parameters(typeof(SearchUsersArgs))]
    public Task<object> SearchUsers(ServiceConfig config, SearchUsersArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/users/search.json?query={Uri.EscapeDataString(r.Query)}");
            return new { Success = true, Users = Prop(doc, "users") };
        });

    [Display(Name = ZendeskMethods.ListMacros)]
    [Description("List available macros (canned ticket actions/responses).")]
    [Parameters(typeof(ListMacrosArgs))]
    public Task<object> ListMacros(ServiceConfig config, ListMacrosArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/macros/active.json?per_page={r.PageSize ?? 50}");
            return new { Success = true, Macros = Prop(doc, "macros") };
        });

    [Display(Name = ZendeskMethods.ApplyMacro)]
    [Description("Apply a macro to a ticket — renders the macro's changes and saves them to the ticket.")]
    [Parameters(typeof(ApplyMacroArgs))]
    public Task<object> ApplyMacro(ServiceConfig config, ApplyMacroArgs r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            // Render the macro against the ticket, then persist the resulting ticket fields.
            var rendered = await client.GetAsync($"/tickets/{r.TicketId}/macros/{r.MacroId}/apply.json");
            if (!rendered.TryGetProperty("result", out var result) || !result.TryGetProperty("ticket", out var ticket))
                return new { Success = false, Error = "Macro did not return a ticket result." };

            var doc = await client.PutAsync($"/tickets/{r.TicketId}.json", new { ticket });
            return new { Success = true, Ticket = Prop(doc, "ticket") };
        });

    // Returns the named property as a JsonElement, or the whole doc if absent.
    private static object Prop(JsonElement doc, string name) =>
        doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty(name, out var el) ? el : doc;

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Zendesk operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
