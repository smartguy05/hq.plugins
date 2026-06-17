using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Safety;
using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MimeKit;

namespace HQ.Plugins.Email;

public class EmailService
{
    private readonly INotificationService _notificationService;
    private readonly LocalEmailStore _store;
    private readonly EmailVectorService _vectorService;
    private readonly EmailSyncEngine _syncEngine;
    private readonly LogDelegate _log;
    private const string PluginName = "HQ.Plugins.Email";

    public EmailService(INotificationService notificationService = null,
        LocalEmailStore store = null, EmailVectorService vectorService = null,
        EmailSyncEngine syncEngine = null, LogDelegate log = null)
    {
        _notificationService = notificationService;
        _store = store;
        _vectorService = vectorService;
        _syncEngine = syncEngine;
        _log = log;
    }

    #region Private Helpers

    private static string TryGetJsonProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static EmailParameters GetMailAccount(ServiceRequest request, ServiceConfig config)
    {
        var mailAccount = config.EmailAccounts.FirstOrDefault(f =>
            string.Equals(f.Name, request.Account, StringComparison.InvariantCultureIgnoreCase));
        mailAccount ??= config.EmailAccounts.FirstOrDefault(f => f.Default);
        if (mailAccount is null)
        {
            throw new Exception("No mail accounts found!");
        }
        return mailAccount;
    }

    internal static async Task<ImapClient> ConnectImapAsync(EmailParameters account, CancellationToken ct = default)
    {
        // Timeout guards against an unreachable host / wrong port hanging forever.
        var client = new ImapClient { Timeout = 20000 };
        await client.ConnectAsync(account.Imap, account.ImapPort, account.UseSsl, ct);
        await client.AuthenticateAsync(account.Username, account.Password, ct);
        return client;
    }

    internal static async Task<SmtpClient> ConnectSmtpAsync(EmailParameters account, CancellationToken ct = default)
    {
        var client = new SmtpClient { Timeout = 20000 };
        await client.ConnectAsync(account.Smtp, account.SmtpPort, MailKit.Security.SecureSocketOptions.Auto, ct);
        await client.AuthenticateAsync(account.Username, account.Password, ct);
        return client;
    }

    /// <summary>
    /// Open a folder for read/write. A null/empty folder (or "INBOX") opens the inbox;
    /// otherwise resolves by name (honoring the Gmail prefix fallback in GetFolderAsync).
    /// </summary>
    private static async Task<IMailFolder> OpenFolderReadWriteAsync(ImapClient client, string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || folder.Equals("INBOX", StringComparison.OrdinalIgnoreCase))
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
            return client.Inbox;
        }

        var mailFolder = await GetFolderAsync(client, folder);
        await mailFolder.OpenAsync(FolderAccess.ReadWrite);
        return mailFolder;
    }

    // --- Shared mailbox mutations: used by both the agent tools and the inbox-viewer
    //     HTTP routes. These perform the real IMAP change and keep the local cache in
    //     sync, with no confirmation flow (a human clicking in the UI / a confirmed
    //     tool call is the authorization). Throw on "not found" so callers can surface it.

    internal static async Task SetSeenFlagAsync(EmailParameters account, string folder, string messageId,
        bool read, LocalEmailStore store)
    {
        using var client = await ConnectImapAsync(account);
        try
        {
            var mailFolder = await OpenFolderReadWriteAsync(client, folder);
            var found = await FindMessageAsync(mailFolder, messageId);
            if (found == null) throw new Exception("Email not found");

            if (read)
                await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Seen, true);
            else
                await found.Value.folder.RemoveFlagsAsync(found.Value.uid, MessageFlags.Seen, true);

            if (store != null)
                await store.UpdateFlagsAsync(messageId, isRead: read);
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    internal static async Task SetFlaggedAsync(EmailParameters account, string folder, string messageId,
        bool flagged, LocalEmailStore store)
    {
        using var client = await ConnectImapAsync(account);
        try
        {
            var mailFolder = await OpenFolderReadWriteAsync(client, folder);
            var found = await FindMessageAsync(mailFolder, messageId);
            if (found == null) throw new Exception("Email not found");

            if (flagged)
                await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Flagged, true);
            else
                await found.Value.folder.RemoveFlagsAsync(found.Value.uid, MessageFlags.Flagged, true);

            if (store != null)
                await store.UpdateFlagsAsync(messageId, isFlagged: flagged);
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>Delete a message from a folder (default inbox) and purge it from the caches.</summary>
    internal static async Task<int> DeleteMessageAsync(EmailParameters account, string folder, string messageId,
        LocalEmailStore store, EmailVectorService vectorService)
    {
        var deletedCount = 0;
        using var client = await ConnectImapAsync(account);
        try
        {
            var mailFolder = await OpenFolderReadWriteAsync(client, folder);
            var uids = await mailFolder.SearchAsync(SearchOptions.All, SearchQuery.HeaderContains("Message-Id", messageId));
            foreach (var uid in uids.UniqueIds)
            {
                await mailFolder.AddFlagsAsync(uid, MessageFlags.Deleted, true);
                deletedCount++;
            }
            await mailFolder.ExpungeAsync();

            if (store != null)
            {
                var localEmail = await store.GetByMessageIdAsync(messageId);
                if (localEmail?.VectorId != null && vectorService != null)
                    await vectorService.DeleteByVectorIdAsync(localEmail.VectorId);
                await store.DeleteByMessageIdAsync(messageId);
            }
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
        return deletedCount;
    }

    /// <summary>
    /// Delete several messages in a single IMAP session, grouped by folder (one expunge
    /// per folder). Far cheaper than one connect per message for bulk deletes. Returns
    /// the number of messages actually removed.
    /// </summary>
    internal static async Task<int> DeleteMessagesAsync(EmailParameters account,
        IEnumerable<(string folder, string messageId)> items,
        LocalEmailStore store, EmailVectorService vectorService)
    {
        var groups = items
            .Where(i => !string.IsNullOrWhiteSpace(i.messageId))
            .GroupBy(i => string.IsNullOrWhiteSpace(i.folder) ? "INBOX" : i.folder);

        var deleted = 0;
        using var client = await ConnectImapAsync(account);
        try
        {
            foreach (var group in groups)
            {
                // Expunge the current folder before opening the next — only one folder
                // is in the selected state per connection.
                var mailFolder = await OpenFolderReadWriteAsync(client, group.Key);
                var expunge = false;
                foreach (var (_, messageId) in group)
                {
                    var uids = await mailFolder.SearchAsync(SearchOptions.All,
                        SearchQuery.HeaderContains("Message-Id", messageId));
                    foreach (var uid in uids.UniqueIds)
                    {
                        await mailFolder.AddFlagsAsync(uid, MessageFlags.Deleted, true);
                        expunge = true;
                        deleted++;
                    }
                    if (store != null)
                    {
                        var local = await store.GetByMessageIdAsync(messageId);
                        if (local?.VectorId != null && vectorService != null)
                            await vectorService.DeleteByVectorIdAsync(local.VectorId);
                        await store.DeleteByMessageIdAsync(messageId);
                    }
                }
                if (expunge) await mailFolder.ExpungeAsync();
            }
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
        return deleted;
    }

    // --- Connection testing (for the config "Test" button). Tries IMAP then SMTP
    //     against the submitted credentials; never throws — failures are reported.

    public record ProtocolTestResult(bool Ok, string Message);
    public record AccountTestResult(ProtocolTestResult Imap, ProtocolTestResult Smtp);

    // Per-protocol timeout for the test button — kept short so the HTTP request always
    // returns well within any reverse-proxy gateway timeout instead of hanging (→ 504).
    private const int TestTimeoutMs = 12000;

    internal static async Task<AccountTestResult> TestAccountAsync(EmailParameters account)
    {
        // Run IMAP and SMTP concurrently so the worst case is one timeout, not two.
        var imapTask = TestImapAsync(account);
        var smtpTask = TestSmtpAsync(account);
        await Task.WhenAll(imapTask, smtpTask);
        return new AccountTestResult(imapTask.Result, smtpTask.Result);
    }

    private static async Task<ProtocolTestResult> TestImapAsync(EmailParameters account)
    {
        if (string.IsNullOrWhiteSpace(account?.Imap))
            return new ProtocolTestResult(false, "No IMAP host configured.");
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        try
        {
            using var client = await ConnectImapAsync(account, cts.Token);
            await client.DisconnectAsync(true);
            return new ProtocolTestResult(true, "Connected and authenticated.");
        }
        catch (Exception ex)
        {
            return new ProtocolTestResult(false, DescribeConnectionError(ex));
        }
    }

    private static async Task<ProtocolTestResult> TestSmtpAsync(EmailParameters account)
    {
        if (string.IsNullOrWhiteSpace(account?.Smtp))
            return new ProtocolTestResult(false, "No SMTP host configured.");
        using var cts = new CancellationTokenSource(TestTimeoutMs);
        try
        {
            using var client = await ConnectSmtpAsync(account, cts.Token);
            await client.DisconnectAsync(true);
            return new ProtocolTestResult(true, "Connected and authenticated.");
        }
        catch (Exception ex)
        {
            return new ProtocolTestResult(false, DescribeConnectionError(ex));
        }
    }

    /// <summary>Map a MailKit connection/auth exception to a concise, user-readable message.</summary>
    public static string DescribeConnectionError(Exception ex)
    {
        return ex switch
        {
            MailKit.Security.AuthenticationException =>
                "Authentication failed — check the username and password (for Gmail/Outlook use an app password).",
            MailKit.Security.SslHandshakeException =>
                "TLS/SSL handshake failed — check the port and the 'Use SSL' setting.",
            System.Net.Sockets.SocketException =>
                "Could not reach the server — check the host and port.",
            OperationCanceledException or TimeoutException =>
                "Connection timed out — check the host, port, and 'Use SSL' setting.",
            _ => ex.Message
        };
    }

    private static async Task<(IMailFolder folder, UniqueId uid)?> FindMessageAsync(IMailFolder folder, string messageId)
    {
        var results = await folder.SearchAsync(SearchOptions.All, SearchQuery.HeaderContains("Message-Id", messageId));
        if (results.UniqueIds.Count == 0)
            return null;
        return (folder, results.UniqueIds[0]);
    }

    private static async Task<IMailFolder> GetFolderAsync(ImapClient client, string name)
    {
        try
        {
            var folder = await client.GetFolderAsync(name);
            if (folder != null) return folder;
        }
        catch { }

        try
        {
            var folder = await client.GetFolderAsync($"[Gmail]/{name}");
            if (folder != null) return folder;
        }
        catch { }

        throw new Exception($"Folder '{name}' not found");
    }

    private static MailMessage MapToMailMessage(MimeMessage message)
    {
        return new MailMessage
        {
            Body = GetEmailBody(message),
            HasAttachments = message.Attachments?.Any() == true,
            MessageId = message.MessageId,
            Date = message.Date,
            Subject = message.Subject,
            Priority = message.Priority,
            Sender = message.Sender?.Address,
            From = string.Join(", ", message.From?.Select(s => s.Name) ?? new List<string>()),
            ReplyTo = string.Join(", ", message.ReplyTo?.Select(s => s.Name) ?? new List<string>()),
            To = string.Join(", ", message.To?.Select(s => s.Name) ?? new List<string>()),
            Bcc = string.Join(", ", message.Bcc?.Select(s => s.Name) ?? new List<string>()),
            Attachments = message.Attachments?.Select(a => a is MimePart mp ? mp.FileName : a.ContentType?.Name).Where(n => n != null)
        };
    }

    private static string GetPreview(string body, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        return body.Length <= maxLength ? body : body[..maxLength] + "...";
    }

    private async Task<object> MarkProvenanceAsync(
        string content, string senderAddress, ServiceConfig config, string provenance)
    {
        if (string.IsNullOrEmpty(content)) return content ?? string.Empty;
        var normalized = senderAddress?.Trim().ToLowerInvariant();
        if (IsInSeed(normalized, config)) return content;
        if (_store != null && await _store.IsTrustedSenderAsync(normalized)) return content;
        return new Untrusted<string>(content, provenance, normalized ?? "unknown");
    }

    private static bool IsInSeed(string normalized, ServiceConfig config)
    {
        if (string.IsNullOrEmpty(normalized) || config.TrustedSenderSeed == null) return false;
        var domain = normalized.Contains('@') ? "@" + normalized.Split('@')[1] : null;
        return config.TrustedSenderSeed.Any(s =>
        {
            var t = s?.Trim().ToLowerInvariant();
            return t == normalized || (domain != null && t == domain);
        });
    }

    private static SearchQuery BuildSearchQuery(ServiceRequest request)
    {
        var queries = new List<SearchQuery>();

        if (!string.IsNullOrWhiteSpace(request.MessageId))
            queries.Add(SearchQuery.HeaderContains("Message-Id", request.MessageId));
        if (request.UnreadOnly)
            queries.Add(SearchQuery.NotSeen);
        if (!string.IsNullOrWhiteSpace(request.Sender))
            queries.Add(SearchQuery.FromContains(request.Sender));
        if (!string.IsNullOrWhiteSpace(request.Subject))
            queries.Add(SearchQuery.SubjectContains(request.Subject));
        if (!string.IsNullOrWhiteSpace(request.To))
            queries.Add(SearchQuery.ToContains(request.To));
        if (!string.IsNullOrWhiteSpace(request.EmailsSentAfter))
            queries.Add(SearchQuery.SentSince(DateTime.Parse(request.EmailsSentAfter)));
        if (!string.IsNullOrWhiteSpace(request.EmailsSentBefore))
            queries.Add(SearchQuery.SentBefore(DateTime.Parse(request.EmailsSentBefore)));

        if (queries.Count == 0)
            return SearchQuery.All;

        var combined = queries[0];
        for (int i = 1; i < queries.Count; i++)
            combined = combined.And(queries[i]);

        return combined;
    }

    static string GetEmailBody(MimeMessage message)
    {
        if (message.Body is Multipart multipart)
        {
            var plainTextPart = multipart
                .OfType<TextPart>()
                .FirstOrDefault(p => !p.IsHtml);

            if (plainTextPart != null)
                return CleanPlainText(plainTextPart.Text);

            var htmlPart = multipart
                .OfType<TextPart>()
                .FirstOrDefault(p => p.IsHtml);

            if (htmlPart != null)
                return ExtractTextFromHtml(htmlPart.Text);
        }
        else if (message.Body is TextPart textPart)
        {
            return textPart.IsHtml
                ? ExtractTextFromHtml(textPart.Text)
                : CleanPlainText(textPart.Text);
        }

        return string.Empty;
    }

    static string CleanPlainText(string text)
    {
        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    static string ExtractTextFromHtml(string html)
    {
        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);
        var text = doc.DocumentNode.InnerText.Trim();
        return CleanHtmlEmail(text);
    }

    static string CleanHtmlEmail(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        input = Regex.Replace(input, @"\\u([0-9a-fA-F]{4})", match =>
        {
            var charCode = Convert.ToInt32(match.Groups[1].Value, 16);
            return char.ConvertFromUtf32(charCode);
        });

        var decoded = HttpUtility.HtmlDecode(input);

        decoded = Regex.Replace(
            decoded,
            @"(https?|ftp)://[^\s/$.?#].[^\s]*|www\.[^\s/$.?#].[^\s]*|[^\s@]+\.(com|net|org|edu|gov|mil|co|io|app|dev|me|info|biz)[^\s,.:;""')}]*",
            "");

        decoded = Regex.Replace(decoded, @"[\u034F\u00AD\u200B-\u200F\u2028-\u202F]+", "");
        decoded = Regex.Replace(decoded, @"[\r\n\t]+", " ");
        decoded = Regex.Replace(decoded, @"\s+", " ");

        return decoded.Trim();
    }

    #endregion

    #region Read-Only Operations

    [Display(Name = "get_email")]
    [Description("Use this tool to get a specific email. Reads from local store first, falls back to IMAP.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email to retrieve"
                    }
                  },
                  "required": ["messageId"]
                }
                """)]
    public async Task<object> GetEmail(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        // Try local store first
        if (_store != null)
        {
            var account = GetMailAccount(request, config);
            var local = await _store.GetByMessageIdAsync(request.MessageId, account.Name);
            if (local != null)
            {
                var body = await MarkProvenanceAsync(local.BodyText, local.FromAddress, config, "email-body");
                return new
                {
                    Success = true,
                    Source = "local",
                    Result = new
                    {
                        local.MessageId,
                        local.Subject,
                        From = local.FromName ?? local.FromAddress,
                        local.ToAddress,
                        local.DateSent,
                        Body = body,
                        local.HasAttachments,
                        local.AttachmentNames,
                        local.IsRead,
                        local.IsFlagged,
                        local.Folder
                    }
                };
            }
        }

        // Fallback to IMAP
        var mailAccount = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(mailAccount);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            var message = await found.Value.folder.GetMessageAsync(found.Value.uid);
            var mapped = MapToMailMessage(message);
            var senderAddr = message.From?.Mailboxes.FirstOrDefault()?.Address ?? message.Sender?.Address;
            var wrappedBody = await MarkProvenanceAsync(mapped.Body, senderAddr, config, "email-body");
            return new
            {
                Success = true,
                Source = "imap",
                Result = new
                {
                    mapped.MessageId,
                    mapped.Subject,
                    mapped.From,
                    mapped.To,
                    mapped.Sender,
                    mapped.ReplyTo,
                    mapped.Bcc,
                    mapped.Date,
                    mapped.Priority,
                    Body = wrappedBody,
                    mapped.HasAttachments,
                    mapped.Attachments
                }
            };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    [Display(Name = "get_drafts")]
    [Description("Use this tool to get a list of draft emails")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> GetDrafts(ServiceConfig config, ServiceRequest request)
    {
        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            var draftsFolder = client.GetFolder(SpecialFolder.Drafts);
            await draftsFolder.OpenAsync(FolderAccess.ReadOnly);

            var messages = new List<MailMessage>();
            var count = Math.Min(draftsFolder.Count, request.MaxReturnedEmails);

            for (var i = 0; i < count; i++)
            {
                var message = await draftsFolder.GetMessageAsync(i);
                messages.Add(MapToMailMessage(message));
            }

            return new { Success = true, Total = draftsFolder.Count, Result = messages };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    [Display(Name = "get_email_summary")]
    [Description("List emails matching filter criteria via IMAP. To find emails from a specific person, use the 'sender' parameter with their email address or name. To find emails about a topic, use 'subject'. All filters are AND-ed together. For full-text search including body content, use search_emails_local instead.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account to access. Optional — uses the default account if not specified."
                    },
                    "searchSubject": {
                      "type": "string",
                      "description": "Client-side partial match on subject line (case-insensitive). Applied after IMAP results are fetched. Use 'subject' for server-side IMAP filtering instead when possible."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The exact Message-Id header to retrieve a specific email."
                    },
                    "unreadOnly": {
                      "type": "boolean",
                      "description": "If true, only return unread/unseen emails. Default false."
                    },
                    "sender": {
                      "type": "string",
                      "description": "Filter by sender (From field). Use this to find emails FROM a specific person — pass their email address or name, e.g. 'john@example.com' or 'John'. This is the correct parameter for person-based searches."
                    },
                    "subject": {
                      "type": "string",
                      "description": "IMAP server-side subject search (partial match). Use this to find emails about a specific topic."
                    },
                    "to": {
                      "type": "string",
                      "description": "Filter by To address. Use this to find emails sent TO a specific person."
                    },
                    "emailsSentAfter": {
                      "type": "string",
                      "description": "ISO datetime string. Only return emails received after this date, e.g. '2025-01-15'."
                    },
                    "emailsSentBefore": {
                      "type": "string",
                      "description": "ISO datetime string. Only return emails received before this date, e.g. '2025-02-01'."
                    },
                    "maxReturnedEmails": {
                      "type": "number",
                      "description": "Maximum number of emails to return. Default 10."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> GetEmailSummary(ServiceConfig config, ServiceRequest request)
    {
        // Try local store first
        if (_store != null)
        {
            var account = GetMailAccount(request, config);
            var totalCount = await _store.GetTotalEmailCountAsync(account.Name);
            if (totalCount > 0)
            {
                var localResults = await _store.SearchAsync(
                    accountName: account.Name,
                    subject: request.SearchSubject ?? request.Subject,
                    sender: request.Sender,
                    maxResults: request.MaxReturnedEmails);

                var summaries = new List<object>();
                foreach (var e in localResults)
                {
                    var preview = await MarkProvenanceAsync(GetPreview(e.BodyText), e.FromAddress, config, "email-body");
                    summaries.Add(new
                    {
                        e.MessageId,
                        e.Subject,
                        From = e.FromName ?? e.FromAddress,
                        Date = e.DateSent,
                        Preview = preview,
                        e.IsRead,
                        e.IsFlagged,
                        e.Folder
                    });
                }

                return new { Success = true, Source = "local", Total = totalCount, Result = summaries };
            }
        }

        // Fallback to IMAP
        var mailAccount = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(mailAccount);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly);

            var searchQuery = BuildSearchQuery(request);
            var uids = await client.Inbox.SearchAsync(searchQuery);

            var emailIds = uids.Distinct().ToList();
            var messageCount = Math.Min(emailIds.Any() ? emailIds.Count : client.Inbox.Count, request.MaxReturnedEmails);

            var summaries = new List<object>();
            if (emailIds.Any())
            {
                foreach (var uid in emailIds.Take(request.MaxReturnedEmails))
                {
                    var message = await client.Inbox.GetMessageAsync(uid);

                    if (!string.IsNullOrWhiteSpace(request.SearchSubject) &&
                        !message.Subject?.Contains(request.SearchSubject, StringComparison.InvariantCultureIgnoreCase) == true)
                        continue;

                    summaries.Add(await BuildSummaryAsync(message, config));
                    if (summaries.Count >= request.MaxReturnedEmails) break;
                }
            }
            else
            {
                for (var i = client.Inbox.Count - 1; i >= 0 && summaries.Count < messageCount; i--)
                {
                    var message = await client.Inbox.GetMessageAsync(i);

                    if (!string.IsNullOrWhiteSpace(request.SearchSubject) &&
                        !message.Subject?.Contains(request.SearchSubject, StringComparison.InvariantCultureIgnoreCase) == true)
                        continue;

                    summaries.Add(await BuildSummaryAsync(message, config));
                }
            }

            return new { Success = true, Source = "imap", Total = emailIds.Any() ? emailIds.Count : client.Inbox.Count, Result = summaries };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    private async Task<object> BuildSummaryAsync(MimeMessage msg, ServiceConfig config)
    {
        var body = GetEmailBody(msg);
        var senderAddr = msg.From?.Mailboxes.FirstOrDefault()?.Address ?? msg.Sender?.Address;
        var preview = await MarkProvenanceAsync(GetPreview(body), senderAddr, config, "email-body");
        return new
        {
            MessageId = msg.MessageId,
            Subject = msg.Subject,
            From = string.Join(", ", msg.From?.Select(s => s.Name) ?? new List<string>()),
            Date = msg.Date,
            Preview = preview,
            Attachments = msg.Attachments?.Select(a => a is MimePart mp ? mp.FileName : a.ContentType?.Name).Where(n => n != null).ToList()
        };
    }

    [Display(Name = "get_attachments")]
    [Description("Use this tool to get the attachments of the supplied email message id.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email to get attachments for"
                    }
                  },
                  "required": ["messageId"]
                }
                """)]
    public async Task<object> GetAttachments(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            var message = await found.Value.folder.GetMessageAsync(found.Value.uid);
            var attachments = new List<object>();

            foreach (var attachment in message.Attachments)
            {
                if (attachment is MimePart mimePart)
                {
                    using var stream = new MemoryStream();
                    await mimePart.Content.DecodeToAsync(stream);
                    var data = Convert.ToBase64String(stream.ToArray());

                    attachments.Add(new
                    {
                        FileName = mimePart.FileName,
                        ContentType = mimePart.ContentType?.MimeType,
                        Size = stream.Length,
                        Data = data
                    });
                }
            }

            return new { Success = true, Result = attachments };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    #endregion

    #region New Tools - Search & Sync

    [Display(Name = "search_emails")]
    [Description("Semantic search across emails using natural language (powered by ChromaDB vector search). Best for vague or conceptual queries like 'emails about the project deadline'. For exact matches by sender, subject, or keyword, use search_emails_local or get_email_summary instead.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "query": {
                      "type": "string",
                      "description": "Natural language search query, e.g. 'emails about the project deadline' or 'messages from John about invoices'"
                    },
                    "maxResults": {
                      "type": "number",
                      "description": "Maximum number of results to return. Default 10."
                    }
                  },
                  "required": ["query"]
                }
                """)]
    public async Task<object> SearchEmails(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new Exception("Query is required for semantic search");

        if (_vectorService == null)
            return new { Success = false, Message = "Semantic search is not configured. Set ChromaUrl and OpenAiApiKey in the plugin config." };

        var maxResults = request.MaxResults ?? 10;
        var results = await _vectorService.SearchAsync(request.Query, maxResults);

        // Enrich with local store data if available
        var enriched = new List<object>();
        foreach (var (messageId, subject, snippet, distance) in results)
        {
            var email = _store != null ? await _store.GetByMessageIdAsync(messageId) : null;
            var rawPreview = email != null ? GetPreview(email.BodyText, 200) : snippet;
            var previewSender = email?.FromAddress;
            var preview = await MarkProvenanceAsync(rawPreview, previewSender, config, "email-body");
            enriched.Add(new
            {
                MessageId = messageId,
                Subject = email?.Subject ?? subject,
                From = email != null ? (email.FromName ?? email.FromAddress) : null,
                Date = email?.DateSent,
                Preview = preview,
                Similarity = 1.0f - distance,
                Folder = email?.Folder
            });
        }

        return new { Success = true, Total = enriched.Count, Result = enriched };
    }

    [Display(Name = "search_emails_local")]
    [Description("Search the local email database by keyword. Use the 'sender' parameter to find emails from a specific person or address (e.g. sender: 'john@example.com' or sender: 'John'). Use 'subject' for subject-line searches. Use 'searchText' only for broad body-text searches. Multiple parameters are AND-ed together.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account to search. Optional, searches all accounts if not specified."
                    },
                    "searchText": {
                      "type": "string",
                      "description": "General search text — matches across subject, sender (address and name), and body using OR logic. Use this for broad searches like a person's name or a keyword. Can be combined with other filters (folder, sender, subject) which are AND-ed."
                    },
                    "folder": {
                      "type": "string",
                      "description": "Limit search to a specific folder (e.g. 'INBOX', 'Sent'). Optional."
                    },
                    "sender": {
                      "type": "string",
                      "description": "Filter by sender email address or display name (partial match). Use this to find emails FROM a specific person, e.g. 'anthony@apetalo.us' or 'Anthony'. This is the correct parameter for person-based searches."
                    },
                    "subject": {
                      "type": "string",
                      "description": "Filter by subject line text (partial match). Overrides searchText for subject matching when both are provided."
                    },
                    "maxResults": {
                      "type": "number",
                      "description": "Maximum number of results to return. Default 20."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> SearchEmailsLocal(ServiceConfig config, ServiceRequest request)
    {
        if (_store == null)
            return new { Success = false, Message = "Local email store is not configured. Set SqliteConnectionString in the plugin config." };

        var account = !string.IsNullOrWhiteSpace(request.Account)
            ? GetMailAccount(request, config)
            : null;

        var maxResults = request.MaxResults ?? 20;
        var results = await _store.SearchAsync(
            accountName: account?.Name,
            folder: request.Folder,
            subject: request.Subject,
            sender: request.Sender,
            searchText: request.SearchText,
            maxResults: maxResults);

        var summaries = new List<object>();
        foreach (var e in results)
        {
            var preview = await MarkProvenanceAsync(GetPreview(e.BodyText, 200), e.FromAddress, config, "email-body");
            summaries.Add(new
            {
                e.MessageId,
                e.Subject,
                From = e.FromName ?? e.FromAddress,
                Date = e.DateSent,
                Preview = preview,
                e.IsRead,
                e.IsFlagged,
                e.Folder
            });
        }

        return new { Success = true, Total = summaries.Count, Result = summaries };
    }

    [Display(Name = "add_trusted_sender")]
    [Description("Add an email address or domain wildcard to the trusted-sender whitelist. " +
                 "Accepts 'alice@example.com' or '@example.com' (trusts all senders from that domain). " +
                 "Use this only after you have verified the sender is legitimate. Senders on the " +
                 "whitelist have their email content passed to you without the untrusted-content wrapper.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "sender": {
                      "type": "string",
                      "description": "Email address or '@domain' wildcard to trust. Case-insensitive. Exact match for addresses; any sender from the domain for wildcards."
                    },
                    "reason": {
                      "type": "string",
                      "description": "Short explanation of why this sender is being trusted. Stored for audit."
                    }
                  },
                  "required": ["sender", "reason"]
                }
                """)]
    public async Task<object> AddTrustedSender(ServiceConfig config, ServiceRequest request)
    {
        if (_store == null)
            return new { Success = false, Message = "Local email store is not configured. Set SqliteConnectionString in the plugin config." };
        if (string.IsNullOrWhiteSpace(request.Sender))
            return new { Success = false, Message = "Sender is required." };
        if (string.IsNullOrWhiteSpace(request.Reason))
            return new { Success = false, Message = "Reason is required." };

        var raw = request.Sender.Trim();
        string normalized;
        if (raw.StartsWith("@"))
        {
            if (!raw.Contains('.') || raw.Any(char.IsWhiteSpace))
                return new { Success = false, Message = "Domain wildcard must look like '@example.com'." };
            normalized = raw.ToLowerInvariant();
        }
        else
        {
            if (!MimeKit.MailboxAddress.TryParse(raw, out var mbox))
                return new { Success = false, Message = $"Could not parse '{raw}' as an email address." };
            normalized = mbox.Address.Trim().ToLowerInvariant();
        }

        await _store.AddTrustedSenderAsync(normalized, request.Reason);
        return new { Success = true, Sender = normalized, Reason = request.Reason };
    }

    [Display(Name = "remove_trusted_sender")]
    [Description("Remove an entry from the agent-managed trusted-sender whitelist. " +
                 "Operator-seeded entries (from the plugin configuration) cannot be removed.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "sender": {
                      "type": "string",
                      "description": "Email address or '@domain' wildcard to remove from the whitelist."
                    }
                  },
                  "required": ["sender"]
                }
                """)]
    public async Task<object> RemoveTrustedSender(ServiceConfig config, ServiceRequest request)
    {
        if (_store == null)
            return new { Success = false, Message = "Local email store is not configured. Set SqliteConnectionString in the plugin config." };
        if (string.IsNullOrWhiteSpace(request.Sender))
            return new { Success = false, Message = "Sender is required." };

        var normalized = request.Sender.Trim().ToLowerInvariant();

        if (IsInSeed(normalized, config))
            return new { Success = false, Message = "Operator-seeded trusted senders cannot be removed. Remove the entry from ServiceConfig.TrustedSenderSeed instead." };

        var removed = await _store.RemoveTrustedSenderAsync(normalized);
        return new { Success = removed, Sender = normalized, Removed = removed };
    }

    [Display(Name = "list_trusted_senders")]
    [Description("List all trusted email senders. Operator-seeded entries (immutable) are separated from agent-added entries (mutable).")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {},
                  "required": []
                }
                """)]
    public async Task<object> ListTrustedSenders(ServiceConfig config, ServiceRequest request)
    {
        var seed = (config.TrustedSenderSeed ?? Array.Empty<string>())
            .Select(s => s?.Trim().ToLowerInvariant())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        var agentAdded = _store != null
            ? await _store.ListTrustedSendersAsync()
            : new List<TrustedSenderRow>();

        return new
        {
            Success = true,
            Seed = seed,
            AgentAdded = agentAdded.Select(r => new { r.Email, r.Reason, r.AddedAt }).ToList()
        };
    }

    [Display(Name = "sync_emails")]
    [Description("Manually trigger email sync to download new emails to the local store. Returns sync status.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "Specific account to sync. Optional, syncs all accounts if not specified."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> SyncEmails(ServiceConfig config, ServiceRequest request)
    {
        if (_syncEngine == null)
            return new { Success = false, Message = "Email sync is not configured. Set SqliteConnectionString in the plugin config." };

        return await _syncEngine.SyncAllAccountsAsync();
    }

    [Display(Name = "get_folders")]
    [Description("List synced email folders with message counts for the specified account.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account. Optional, uses default account."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> GetFolders(ServiceConfig config, ServiceRequest request)
    {
        if (_store == null)
            return new { Success = false, Message = "Local email store is not configured. Set SqliteConnectionString in the plugin config." };

        var account = GetMailAccount(request, config);
        var folders = await _store.GetFoldersAsync(account.Name);

        var result = folders.Select(f => new
        {
            Name = f.FolderName,
            Messages = f.MessageCount,
            Unread = f.UnreadCount,
            SpecialUse = f.SpecialUse
        }).ToList();

        return new { Success = true, Account = account.Name, Result = result };
    }

    #endregion

    #region Write Operations - No Confirmation

    [Display(Name = "mark_as_read")]
    [Description("Use this tool to mark the specified email as read.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email message to update"
                    },
                    "markAsRead": {
                      "type": "boolean",
                      "description": "True the email with be marked as Read, False the email will be marked as Unread."
                    }
                  },
                  "required": ["messageId","markAsRead"]
                }
                """)]
    public async Task<object> MarkAsRead(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        await SetSeenFlagAsync(account, request.Folder, request.MessageId, request.MarkAsRead == true, _store);
        var status = request.MarkAsRead == true ? "read" : "unread";
        return new { Success = true, Message = $"Email marked as {status}" };
    }

    [Display(Name = "flag_email")]
    [Description("Use this tool to flag/unflag an email using the standard IMAP Flagged flag")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email message to update"
                    },
                    "flag": {
                      "type": "boolean",
                      "description": "Set to true to flag the email, false to unflag"
                    }
                  },
                  "required": ["messageId","flag"]
                }
                """)]
    public async Task<object> FlagEmail(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        await SetFlaggedAsync(account, request.Folder, request.MessageId, request.Flag == true, _store);
        var status = request.Flag == true ? "flagged" : "unflagged";
        return new { Success = true, Message = $"Email {status}" };
    }

    [Display(Name = "move_to_folder")]
    [Description("Use this tool to move the specified email to the specified email folder.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email message to move"
                    },
                    "folder": {
                      "type": "string",
                      "description": "The folder you wish to move the email to"
                    }
                  },
                  "required": ["messageId","folder"]
                }
                """)]
    public async Task<object> MoveToFolder(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        if (string.IsNullOrWhiteSpace(request.Folder))
            throw new Exception("Folder is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            var destFolder = await GetFolderAsync(client, request.Folder);
            await destFolder.OpenAsync(FolderAccess.ReadWrite);
            var newUid = await found.Value.folder.MoveToAsync(found.Value.uid, destFolder);

            // Update local store
            if (_store != null && newUid.HasValue)
                await _store.UpdateFolderAsync(request.MessageId, request.Folder, newUid.Value.Id);

            return new { Success = true, Message = $"Email moved to {request.Folder}" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    #endregion

    #region Write Operations - With Confirmation

    [Display(Name = "send_email")]
    [Description("Use this tool to send emails from the user's already set up email account. Always format the email body using HTML for attractive styling, including appropriate use of paragraphs, headings, bold text, lists, and other HTML tags to improve readability and visual appeal. Use this tool when asked something like 'Send an email to mom telling her I will be late'")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "subject": {
                      "type": "string",
                      "description": "The subject of the email to send."
                    },
                    "body": {
                      "type": "string",
                      "description": "The HTML formatted body of the email to send. This should be well-structured and styled using HTML tags like <p>, <h1>, <strong>, <ul>, <ol>, etc."
                    },
                    "recipientName": {
                      "type": "string",
                      "description": "The name of the person receiving the email."
                    },
                    "to": {
                      "type": "string",
                      "description": "The email address to send the email to."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the draft email to send. Use this if you have already created an draft and want to send it."
                    }
                  },
                  "required": []
                }
                """)]
    [SupportsConfirmation]
    public async Task<object> SendEmail(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) && string.IsNullOrWhiteSpace(request.To))
            throw new Exception("Must supply existing draft email Id or a To address to send an email.");

        if (config.RequiresConfirmation)
        {
            if (_notificationService == null)
            {
                _log?.Invoke(LogLevel.Warning, "Skipping confirmation — no notification service configured");
            }
            else if (string.IsNullOrWhiteSpace(request.ConfirmationId))
            {
                var confirmation = new Confirmation
                {
                    ConfirmationMessage = "Are you sure you want to send this email?",
                    Content = request.Body,
                    Options = new Dictionary<string, bool>
                    {
                        { "Yes", true },
                        { "No", false }
                    },
                    Id = Guid.NewGuid()
                };
                return await _notificationService.RequestConfirmation(PluginName, confirmation, request);
            }
            else if (!_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
            {
                return new { Success = false, Error = "Unable to send email without valid confirmation" };
            }
        }

        return await ExecuteSendEmailAsync(config, request);
    }

    private async Task<object> ExecuteSendEmailAsync(ServiceConfig config, ServiceRequest request)
    {
        var account = GetMailAccount(request, config);

        MimeMessage message;
        if (!string.IsNullOrWhiteSpace(request.MessageId))
        {
            using var imapClient = await ConnectImapAsync(account);
            try
            {
                var draftsFolder = imapClient.GetFolder(SpecialFolder.Drafts);
                await draftsFolder.OpenAsync(FolderAccess.ReadWrite);
                var found = await FindMessageAsync(draftsFolder, request.MessageId);
                if (found == null)
                    return new { Success = false, Message = "Draft not found" };

                message = await found.Value.folder.GetMessageAsync(found.Value.uid);

                await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Deleted, true);
                await found.Value.folder.ExpungeAsync();
            }
            finally
            {
                await imapClient.DisconnectAsync(true);
            }
        }
        else
        {
            message = new MimeMessage
            {
                Subject = request.Subject,
                Body = new TextPart("html") { Text = request.Body }
            };
            message.From.Add(new MailboxAddress(account.DisplayName, account.Email));
            message.To.Add(new MailboxAddress(request.RecipientName, request.To));
        }

        using var smtpClient = await ConnectSmtpAsync(account);
        try
        {
            await smtpClient.SendAsync(message);
            return new { Success = true, Message = "Email Sent!" };
        }
        finally
        {
            await smtpClient.DisconnectAsync(true);
        }
    }

    [Display(Name = "delete_email")]
    [Description("Use this tool to delete emails from the user's already set up email account, for example when asked something like 'delete that email', or 'delete my last email'")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email message to delete"
                    }
                  },
                  "required": ["messageId"]
                }
                """)]
    [SupportsConfirmation]
    public async Task<object> DeleteEmail(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        if (config.RequiresConfirmation)
        {
            if (_notificationService == null)
            {
                _log?.Invoke(LogLevel.Warning, "Skipping confirmation — no notification service configured");
            }
            else if (string.IsNullOrWhiteSpace(request.ConfirmationId))
            {
                return await _notificationService.RequestConfirmation(
                    PluginName,
                    new Confirmation
                    {
                        ConfirmationMessage = "Are you sure you want to delete this email?",
                        Content = request.Body,
                        Options = new Dictionary<string, bool>
                        {
                            { "Yes", true },
                            { "No", false }
                        },
                        Id = Guid.NewGuid()
                    },
                    request);
            }
            else if (!_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
            {
                return new { Success = false, Error = "Unable to delete email without valid confirmation" };
            }
        }

        return await ExecuteDeleteEmailAsync(config, request);
    }

    private async Task<object> ExecuteDeleteEmailAsync(ServiceConfig config, ServiceRequest request)
    {
        var account = GetMailAccount(request, config);
        var deletedCount = await DeleteMessageAsync(account, request.Folder, request.MessageId, _store, _vectorService);

        var message = deletedCount > 0 ? "Email(s) Deleted!" : "Unable to delete email(s)!";
        if (config.RequiresConfirmation && _notificationService != null)
            return await _notificationService.SendNotification(message);
        return new { Success = true, Message = message };
    }

    #endregion

    #region Draft CRUD

    [Display(Name = "create_draft")]
    [Description("Use this tool to create a draft email using the supplied criteria.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "subject": {
                      "type": "string",
                      "description": "The subject of the email to send."
                    },
                    "body": {
                      "type": "string",
                      "description": "The HTML formatted body of the email to send. This should be well-structured and styled using HTML tags like <p>, <h1>, <strong>, <ul>, <ol>, etc."
                    },
                    "recipientName": {
                      "type": "string",
                      "description": "The name of the person receiving the email."
                    },
                    "to": {
                      "type": "string",
                      "description": "The email address to send the email to."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> CreateDraft(ServiceConfig config, ServiceRequest request)
    {
        var account = GetMailAccount(request, config);

        var message = new MimeMessage
        {
            Subject = request.Subject,
            Body = new TextPart("html") { Text = request.Body ?? string.Empty }
        };
        message.From.Add(new MailboxAddress(account.DisplayName, account.Email));
        if (!string.IsNullOrWhiteSpace(request.To))
            message.To.Add(new MailboxAddress(request.RecipientName, request.To));

        using var client = await ConnectImapAsync(account);
        try
        {
            var draftsFolder = client.GetFolder(SpecialFolder.Drafts);
            await draftsFolder.OpenAsync(FolderAccess.ReadWrite);
            var uid = await draftsFolder.AppendAsync(message, MessageFlags.Draft);

            return new { Success = true, Message = "Draft created", MessageId = message.MessageId };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    [Display(Name = "delete_draft")]
    [Description("Use this tool to delete a saved draft email.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the draft email to delete"
                    }
                  },
                  "required": ["messageId"]
                }
                """)]
    public async Task<object> DeleteDraft(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            var draftsFolder = client.GetFolder(SpecialFolder.Drafts);
            await draftsFolder.OpenAsync(FolderAccess.ReadWrite);

            var found = await FindMessageAsync(draftsFolder, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Draft not found" };

            await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Deleted, true);
            await found.Value.folder.ExpungeAsync();

            return new { Success = true, Message = "Draft deleted" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    [Display(Name = "add_attachment_to_draft")]
    [Description("Use this tool to add attachments to a previously saved draft email.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email to add the attachment to"
                    },
                    "attachment": {
                      "type": "object",
                      "description": "The attachment to add to the draft email",
                      "properties": {
                        "fileName": { "type": "string", "description": "The name of the file (e.g. report.pdf)" },
                        "contentType": { "type": "string", "description": "MIME type (e.g. application/pdf, text/plain). Defaults to application/octet-stream if not provided." },
                        "data": { "type": "string", "description": "Base64-encoded file content" }
                      },
                      "required": ["fileName", "data"]
                    }
                  },
                  "required": ["messageId"]
                }
                """)]
    public async Task<object> AddAttachmentToDraft(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            var draftsFolder = client.GetFolder(SpecialFolder.Drafts);
            await draftsFolder.OpenAsync(FolderAccess.ReadWrite);

            var found = await FindMessageAsync(draftsFolder, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Draft not found" };

            var originalMessage = await found.Value.folder.GetMessageAsync(found.Value.uid);

            var attachmentJson = request.Attachment?.ToString();
            if (string.IsNullOrWhiteSpace(attachmentJson))
                return new { Success = false, Message = "Attachment data is required" };

            var attachmentData = JsonSerializer.Deserialize<JsonElement>(attachmentJson);

            // Support both camelCase and lowercase property names from LLM
            string fileName = TryGetJsonProperty(attachmentData, "fileName", "filename", "name");
            string contentType = TryGetJsonProperty(attachmentData, "contentType", "contenttype", "mimeType", "mime_type")
                                 ?? "application/octet-stream";
            string base64Content = TryGetJsonProperty(attachmentData, "data", "content", "base64");

            if (string.IsNullOrWhiteSpace(fileName))
                return new { Success = false, Message = "Attachment fileName is required" };
            if (string.IsNullOrWhiteSpace(base64Content))
                return new { Success = false, Message = "Attachment data (base64) is required" };

            var data = Convert.FromBase64String(base64Content);

            var builder = new BodyBuilder();

            if (originalMessage.HtmlBody != null)
                builder.HtmlBody = originalMessage.HtmlBody;
            if (originalMessage.TextBody != null)
                builder.TextBody = originalMessage.TextBody;

            foreach (var existingAttachment in originalMessage.Attachments)
            {
                if (existingAttachment is MimePart mp)
                {
                    using var stream = new MemoryStream();
                    await mp.Content.DecodeToAsync(stream);
                    builder.Attachments.Add(mp.FileName, stream.ToArray(), ContentType.Parse(mp.ContentType.MimeType));
                }
            }

            builder.Attachments.Add(fileName, data, ContentType.Parse(contentType));

            var newMessage = new MimeMessage();
            newMessage.From.AddRange(originalMessage.From);
            newMessage.To.AddRange(originalMessage.To);
            newMessage.Cc.AddRange(originalMessage.Cc);
            newMessage.Bcc.AddRange(originalMessage.Bcc);
            newMessage.Subject = originalMessage.Subject;
            newMessage.Body = builder.ToMessageBody();

            await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Deleted, true);
            await found.Value.folder.ExpungeAsync();
            await draftsFolder.AppendAsync(newMessage, MessageFlags.Draft);

            return new { Success = true, Message = "Attachment added to draft", MessageId = newMessage.MessageId };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    [Display(Name = "remove_attachment_from_draft")]
    [Description("Use this tool to remove an attachment from a previously saved draft email.")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email to remove the attachment from"
                    },
                    "attachment": {
                      "type": "string",
                      "description": "The id or filename of the attachment to remove from the draft email."
                    }
                  },
                  "required": ["messageId"]
                }
                """)]
    public async Task<object> RemoveAttachmentFromDraft(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            var draftsFolder = client.GetFolder(SpecialFolder.Drafts);
            await draftsFolder.OpenAsync(FolderAccess.ReadWrite);

            var found = await FindMessageAsync(draftsFolder, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Draft not found" };

            var originalMessage = await found.Value.folder.GetMessageAsync(found.Value.uid);

            var attachmentToRemove = request.Attachment?.ToString();
            if (string.IsNullOrWhiteSpace(attachmentToRemove))
                return new { Success = false, Message = "Attachment identifier is required" };

            var builder = new BodyBuilder();

            if (originalMessage.HtmlBody != null)
                builder.HtmlBody = originalMessage.HtmlBody;
            if (originalMessage.TextBody != null)
                builder.TextBody = originalMessage.TextBody;

            bool removed = false;
            foreach (var existingAttachment in originalMessage.Attachments)
            {
                if (existingAttachment is MimePart mp)
                {
                    if (string.Equals(mp.FileName, attachmentToRemove, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(mp.ContentId, attachmentToRemove, StringComparison.OrdinalIgnoreCase))
                    {
                        removed = true;
                        continue;
                    }

                    using var stream = new MemoryStream();
                    await mp.Content.DecodeToAsync(stream);
                    builder.Attachments.Add(mp.FileName, stream.ToArray(), ContentType.Parse(mp.ContentType.MimeType));
                }
            }

            if (!removed)
                return new { Success = false, Message = $"Attachment '{attachmentToRemove}' not found" };

            var newMessage = new MimeMessage();
            newMessage.From.AddRange(originalMessage.From);
            newMessage.To.AddRange(originalMessage.To);
            newMessage.Cc.AddRange(originalMessage.Cc);
            newMessage.Bcc.AddRange(originalMessage.Bcc);
            newMessage.Subject = originalMessage.Subject;
            newMessage.Body = builder.ToMessageBody();

            await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Deleted, true);
            await found.Value.folder.ExpungeAsync();
            await draftsFolder.AppendAsync(newMessage, MessageFlags.Draft);

            return new { Success = true, Message = "Attachment removed from draft", MessageId = newMessage.MessageId };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    #endregion
}
