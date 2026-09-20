using System.Net;
using System.Net.Sockets;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;
using Microsoft.AspNetCore.Authorization;
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
        //
        // WP6B-11: this is the only Email route with no agent/tenant check (there is no
        // agentId — the caller supplies raw connection details), so any authenticated user
        // could point Imap/Smtp at an internal or metadata-service host:port and use the
        // differentiated auth/TLS/unreachable/timeout message as a port-scanning oracle.
        // Locked down three ways: gated behind the TenantAdmin policy, any host that
        // resolves to a private/loopback/link-local/metadata address is blanked out before
        // EmailService ever opens a socket to it (reported back as "not configured" rather
        // than connected-to), and the response carries only a coarse ok/not-ok per protocol
        // — the differentiated message never leaves this endpoint.
        routes.MapPost("/test-account", async (EmailParameters account) =>
            account == null ? Results.BadRequest("Missing account") : Results.Ok(await BuildTestAccountResultAsync(account)))
            .RequireAuthorization("TenantAdmin");

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

    // --- WP6B-11: SSRF/port-scan guard for /test-account -------------------------------

    /// <summary>
    /// Blanks out any Imap/Smtp host that resolves to a private/loopback/link-local/
    /// metadata destination (so EmailService reports it as "not configured" instead of
    /// connecting to it), then returns only a coarse ok/not-ok per protocol — never the
    /// differentiated auth/TLS/unreachable/timeout message <see cref="EmailService"/>
    /// otherwise produces, which would let an unprivileged caller fingerprint whatever is
    /// listening on an internal host:port. Internal (not private) so tests can call it
    /// directly without going through the route's model binding / HTTP pipeline.
    /// </summary>
    internal static async Task<object> BuildTestAccountResultAsync(EmailParameters account)
    {
        var safeAccount = account with
        {
            Imap = IsHostAllowed(account.Imap, out _) ? account.Imap : null,
            Smtp = IsHostAllowed(account.Smtp, out _) ? account.Smtp : null
        };

        var r = await EmailService.TestAccountAsync(safeAccount);
        return new
        {
            imap = new { ok = r.Imap.Ok },
            smtp = new { ok = r.Smtp.Ok }
        };
    }

    // Docker-compose service names for this platform's own internal dependencies. Mirrors
    // HQ.Models.Safety.UrlGuardOptions.DefaultBlockedHosts (this project is still on
    // HQ.Models 2.8.0, which predates UrlGuard, so the list is duplicated rather than
    // referenced — keep the two in sync if either changes).
    private static readonly HashSet<string> BlockedComposeHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "hq-postgres", "hq-redis", "hq-chromadb", "postgres", "redis", "chromadb"
    };

    /// <summary>
    /// True when <paramref name="host"/> is safe for this process to open an outbound
    /// IMAP/SMTP connection to. A blank host is "allowed" here — EmailService already
    /// reports that as "no host configured" without attempting a connection.
    ///
    /// Adversarial-review follow-up: a plain hostname that is not an IP literal and not
    /// one of the hardcoded internal names used to be allowed unconditionally — but a
    /// resolvable hostname (an internal admin host, or an attacker-owned domain pointed
    /// at 169.254.169.254 or a private IP) is exactly the realistic version of this
    /// finding's own repro (a TenantAdmin-controlled Imap/Smtp host). This project can't
    /// reference HQ.Models.Safety.UrlGuard directly (still on HQ.Models 2.8.0 — see the
    /// note on <see cref="BlockedComposeHosts"/>), so DNS resolution is duplicated here
    /// as plain BCL logic, following the same pattern UrlGuardOptions.ResolveHost uses:
    /// default to real <see cref="Dns.GetHostAddresses(string)"/>, and check every
    /// resolved address with the same rules as a literal IP.
    /// </summary>
    private static bool IsHostAllowed(string host, out string reason, Func<string, IEnumerable<IPAddress>> resolveHost = null)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            reason = null;
            return true;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            reason = "host is blocked (localhost).";
            return false;
        }

        if (BlockedComposeHosts.Contains(host))
        {
            reason = "host is blocked.";
            return false;
        }

        if (IPAddress.TryParse(host, out var ip))
            return IsAddressAllowed(ip, out reason);

        var resolver = resolveHost ?? Dns.GetHostAddresses;
        IEnumerable<IPAddress> addresses;
        try
        {
            addresses = resolver(host) ?? Array.Empty<IPAddress>();
        }
        catch (Exception)
        {
            // Could not resolve: not itself a reason to block. EmailService's own
            // connection attempt will fail on its own (and reports only a coarse ok/not-ok,
            // per WP6B-11) — there is no oracle value left to leak by letting that happen.
            reason = null;
            return true;
        }

        foreach (var address in addresses)
        {
            if (!IsAddressAllowed(address, out reason))
                return false;
        }

        reason = null;
        return true;
    }

    /// <summary>Rejects loopback, unspecified, link-local (incl. cloud metadata endpoints
    /// at 169.254.169.254) and the RFC1918/CGNAT private ranges.</summary>
    private static bool IsAddressAllowed(IPAddress address, out string reason)
    {
        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        if (IPAddress.IsLoopback(ip)) { reason = "address is a loopback address."; return false; }
        if (ip.Equals(IPAddress.Any)) { reason = "address is 0.0.0.0."; return false; }
        if (ip.Equals(IPAddress.IPv6Any)) { reason = "address is the IPv6 unspecified address [::]."; return false; }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) { reason = "address is in 0.0.0.0/8."; return false; }
            if (b[0] == 169 && b[1] == 254) { reason = "address is link-local (169.254.0.0/16) — this includes cloud metadata endpoints."; return false; }
            if (b[0] == 10) { reason = "address is in the private range 10.0.0.0/8."; return false; }
            if (b[0] == 172 && b[1] is >= 16 and <= 31) { reason = "address is in the private range 172.16.0.0/12."; return false; }
            if (b[0] == 192 && b[1] == 168) { reason = "address is in the private range 192.168.0.0/16."; return false; }
            if (b[0] == 100 && b[1] is >= 64 and <= 127) { reason = "address is in the carrier-grade NAT range 100.64.0.0/10."; return false; }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal) { reason = "address is IPv6 link-local (fe80::/10)."; return false; }
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) { reason = "address is IPv6 unique-local (fc00::/7)."; return false; }
        }

        reason = null;
        return true;
    }

    private static Task NoopLog(LogLevel level, string message, Exception ex = null) => Task.CompletedTask;

    public record ActionRequest(string AgentId, string Account, string Folder, string MessageId, bool? Value);
    public record BulkDeleteItem(string Folder, string MessageId);
    public record BulkDeleteRequest(string AgentId, string Account, List<BulkDeleteItem> Items);
}
