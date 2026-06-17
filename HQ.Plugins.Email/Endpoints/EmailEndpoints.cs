using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HQ.Plugins.Email.Endpoints;

/// <summary>
/// HTTP routes backing the inbox-viewer UI and the config "Test" button. Mounted by the
/// host under /api/plugins/HQ.Plugins.Email/data/* (already [Authorize]-gated).
///
/// Reads come from the per-agent synced SQLite cache (no credentials needed). Writes and
/// the account test hit the real mailbox over IMAP/SMTP. Every per-agent endpoint resolves
/// the agent's decrypted config through <see cref="IPluginConfigProvider"/>, which is
/// tenant-scoped — a null result means the agent has no Email config or is outside the
/// caller's organization, so it doubles as the access gate.
/// </summary>
public static class EmailEndpoints
{
    private const string PluginName = "HQ.Plugins.Email";

    public static void Map(IEndpointRouteBuilder routes)
    {
        // Agents with a synced inbox that the caller may view.
        routes.MapGet("/agents", async (HttpContext ctx) =>
        {
            var dir = EmailPaths.EmailDataDir();
            var result = new List<object>();
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "agent-*-emails.db"))
                {
                    var agentId = EmailPaths.AgentIdFromFileName(Path.GetFileName(file));
                    var config = await ResolveAgentConfigAsync(ctx, agentId);
                    if (config == null) continue; // not in caller's org / no email config

                    var accounts = (config.EmailAccounts ?? Enumerable.Empty<EmailParameters>())
                        .Select(a => new { name = a.Name, email = a.Email })
                        .ToList();

                    var total = 0;
                    using (var store = OpenStore(agentId))
                        if (store != null) total = await store.GetTotalEmailCountAsync();

                    result.Add(new { agentId, accounts, total });
                }
            }
            return Results.Ok(result);
        });

        // Folders for an agent's account (with counts), from the cache.
        routes.MapGet("/folders", async (HttpContext ctx, string agentId, string account) =>
        {
            var config = await ResolveAgentConfigAsync(ctx, agentId);
            if (config == null) return Results.NotFound();
            using var store = OpenStore(agentId);
            if (store == null) return Results.Ok(Array.Empty<object>());

            var acct = ResolveAccount(config, account)?.Name;
            var folders = await store.GetFoldersAsync(acct);
            return Results.Ok(folders.Select(f => new
            {
                name = f.FolderName,
                messages = f.MessageCount,
                unread = f.UnreadCount,
                specialUse = f.SpecialUse
            }));
        });

        // Message summaries for a folder (or a keyword search across the account).
        routes.MapGet("/messages", async (HttpContext ctx, string agentId, string account, string folder, string search, int? max) =>
        {
            var config = await ResolveAgentConfigAsync(ctx, agentId);
            if (config == null) return Results.NotFound();
            using var store = OpenStore(agentId);
            if (store == null) return Results.Ok(Array.Empty<object>());

            var acct = ResolveAccount(config, account)?.Name;
            var limit = Math.Clamp(max ?? 100, 1, 500);

            var list = !string.IsNullOrWhiteSpace(search)
                ? await store.SearchAsync(accountName: acct, folder: NullIfEmpty(folder), searchText: search, maxResults: limit)
                : await store.GetByFolderAsync(acct, string.IsNullOrWhiteSpace(folder) ? "INBOX" : folder, limit);

            return Results.Ok(list.Select(Summary));
        });

        // Full message (incl. body + attachment names) from the cache.
        routes.MapGet("/message", async (HttpContext ctx, string agentId, string messageId) =>
        {
            var config = await ResolveAgentConfigAsync(ctx, agentId);
            if (config == null) return Results.NotFound();
            using var store = OpenStore(agentId);
            if (store == null) return Results.NotFound();

            var e = await store.GetByMessageIdAsync(messageId);
            if (e == null) return Results.NotFound();
            return Results.Ok(new
            {
                messageId = e.MessageId,
                subject = e.Subject,
                from = e.FromName ?? e.FromAddress,
                fromAddress = e.FromAddress,
                to = e.ToAddress,
                cc = e.CcAddress,
                date = e.DateSent,
                isRead = e.IsRead,
                isFlagged = e.IsFlagged,
                hasAttachments = e.HasAttachments,
                attachmentNames = e.AttachmentNames,
                folder = e.Folder,
                bodyHtml = e.BodyHtml,
                bodyText = e.BodyText
            });
        });

        // --- Actions against the real mailbox (cache kept in sync by the helpers).

        routes.MapPost("/actions/mark-read", (HttpContext ctx, ActionRequest body) =>
            RunActionAsync(ctx, body, (account, store) =>
                EmailService.SetSeenFlagAsync(account, body.Folder, body.MessageId, body.Value ?? true, store)));

        routes.MapPost("/actions/flag", (HttpContext ctx, ActionRequest body) =>
            RunActionAsync(ctx, body, (account, store) =>
                EmailService.SetFlaggedAsync(account, body.Folder, body.MessageId, body.Value ?? true, store)));

        routes.MapPost("/actions/delete", (HttpContext ctx, ActionRequest body) =>
            RunActionAsync(ctx, body, (account, store) =>
                EmailService.DeleteMessageAsync(account, body.Folder, body.MessageId, store, null)));

        // Bulk delete several selected messages in one IMAP session.
        routes.MapPost("/actions/delete-bulk", async (HttpContext ctx, BulkDeleteRequest body) =>
        {
            if (body?.Items == null || body.Items.Count == 0)
                return Results.BadRequest("No items");
            var config = await ResolveAgentConfigAsync(ctx, body.AgentId);
            if (config == null) return Results.NotFound();
            var account = ResolveAccount(config, body.Account);
            if (account == null) return Results.BadRequest("No matching account");
            try
            {
                using var store = OpenStore(body.AgentId);
                var items = body.Items.Select(i => (i.Folder, i.MessageId));
                var deleted = await EmailService.DeleteMessagesAsync(account, items, store, null);
                return Results.Ok(new { success = true, deleted });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = EmailService.DescribeConnectionError(ex) });
            }
        });

        // Verify an account's IMAP/SMTP credentials (config "Test" button). Tests the
        // submitted account as-is — no agent context or stored config involved.
        routes.MapPost("/test-account", async (EmailParameters account) =>
        {
            if (account == null) return Results.BadRequest("Missing account");
            var r = await EmailService.TestAccountAsync(account);
            return Results.Ok(new
            {
                imap = new { ok = r.Imap.Ok, message = r.Imap.Message },
                smtp = new { ok = r.Smtp.Ok, message = r.Smtp.Message }
            });
        });

        // On-demand sync for an agent ("Sync now"). Runs a one-off sync with the agent's
        // decrypted config into the same cache the background engine uses.
        routes.MapPost("/sync", async (HttpContext ctx, string agentId) =>
        {
            var config = await ResolveAgentConfigAsync(ctx, agentId);
            if (config == null) return Results.NotFound();
            try
            {
                Directory.CreateDirectory(EmailPaths.EmailDataDir());
                using var store = new LocalEmailStore(EmailPaths.ResolveConnectionString(agentId));
                var engine = new EmailSyncEngine(store, null, config, NoopLog);
                var result = await engine.SyncAllAccountsAsync();
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, message = EmailService.DescribeConnectionError(ex) });
            }
        });
    }

    // --- helpers ---------------------------------------------------------------

    private static async Task<IResult> RunActionAsync(HttpContext ctx, ActionRequest body,
        Func<EmailParameters, LocalEmailStore, Task> action)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.MessageId))
            return Results.BadRequest("messageId is required");

        var config = await ResolveAgentConfigAsync(ctx, body.AgentId);
        if (config == null) return Results.NotFound();

        var account = ResolveAccount(config, body.Account);
        if (account == null) return Results.BadRequest("No matching account");

        try
        {
            using var store = OpenStore(body.AgentId);
            await action(account, store);
            return Results.Ok(new { success = true });
        }
        catch (Exception ex)
        {
            return Results.Ok(new { success = false, message = EmailService.DescribeConnectionError(ex) });
        }
    }

    private static async Task<ServiceConfig> ResolveAgentConfigAsync(HttpContext ctx, string agentId)
    {
        if (!Guid.TryParse(agentId, out var guid)) return null;
        var provider = ctx.RequestServices.GetService<IPluginConfigProvider>();
        if (provider == null) return null;
        var json = await provider.GetDecryptedPluginConfigJsonAsync(guid, PluginName);
        return string.IsNullOrWhiteSpace(json) ? null : json.ReadPluginConfig<ServiceConfig>();
    }

    /// <summary>Open an agent's cache read-only; null when it hasn't synced yet.</summary>
    private static LocalEmailStore OpenStore(string agentId)
    {
        var path = EmailPaths.DbPath(agentId);
        return File.Exists(path) ? new LocalEmailStore(EmailPaths.ResolveConnectionString(agentId)) : null;
    }

    private static EmailParameters ResolveAccount(ServiceConfig config, string account)
    {
        var accounts = config.EmailAccounts ?? Enumerable.Empty<EmailParameters>();
        var match = accounts.FirstOrDefault(a =>
            string.Equals(a.Name, account, StringComparison.OrdinalIgnoreCase));
        return match ?? accounts.FirstOrDefault(a => a.Default) ?? accounts.FirstOrDefault();
    }

    private static object Summary(LocalEmail e) => new
    {
        messageId = e.MessageId,
        subject = e.Subject,
        from = e.FromName ?? e.FromAddress,
        fromAddress = e.FromAddress,
        date = e.DateSent,
        isRead = e.IsRead,
        isFlagged = e.IsFlagged,
        hasAttachments = e.HasAttachments,
        folder = e.Folder
    };

    private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static Task NoopLog(LogLevel level, string message, Exception ex = null) => Task.CompletedTask;

    public record ActionRequest(string AgentId, string Account, string Folder, string MessageId, bool? Value);
    public record BulkDeleteItem(string Folder, string MessageId);
    public record BulkDeleteRequest(string AgentId, string Account, List<BulkDeleteItem> Items);
}
