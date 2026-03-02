using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using HQ.Models;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
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
    private const string PluginName = "HQ.Plugins.Email";

    public EmailService(INotificationService notificationService = null)
    {
        _notificationService = notificationService;
    }

    #region Private Helpers

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

    private static async Task<ImapClient> ConnectImapAsync(EmailParameters account)
    {
        var client = new ImapClient();
        await client.ConnectAsync(account.Imap, account.ImapPort, account.UseSsl);
        await client.AuthenticateAsync(account.Username, account.Password);
        return client;
    }

    private static async Task<SmtpClient> ConnectSmtpAsync(EmailParameters account)
    {
        var client = new SmtpClient();
        await client.ConnectAsync(account.Smtp, account.SmtpPort);
        await client.AuthenticateAsync(account.Username, account.Password);
        return client;
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
        // Try direct name first
        try
        {
            var folder = await client.GetFolderAsync(name);
            if (folder != null) return folder;
        }
        catch { /* fall through to Gmail prefix */ }

        // Try with [Gmail]/ prefix
        try
        {
            var folder = await client.GetFolderAsync($"[Gmail]/{name}");
            if (folder != null) return folder;
        }
        catch { /* not found */ }

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

    #region Read-Only Operations (Group A)

    /// <summary>
    /// Gets a specific email by the supplied Message Id
    /// </summary>
    [Display(Name = "get_email")]
    [Description("Use this tool to get a specific email")]
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

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            var message = await found.Value.folder.GetMessageAsync(found.Value.uid);
            return new { Success = true, Result = MapToMailMessage(message) };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Retrieves a list of draft emails.
    /// </summary>
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

    /// <summary>
    /// Returns a summary of emails based on the supplied filter criteria.
    /// </summary>
    [Display(Name = "get_email_summary")]
    [Description("Use this tool to get a list of emails using the supplied criteria. You might use this if the user asks something like 'Who sent that last email?', or 'When did I get that email from Google'")]
    [Parameters("""
                {
                  "type": "object",
                  "properties": {
                    "account": {
                      "type": "string",
                      "description": "The email account you want to access. This is optional, if not supplied the default account will be used."
                    },
                    "searchSubject": {
                      "type": "string",
                      "description": "The subject of the email to search for. This is optional."
                    },
                    "messageId": {
                      "type": "string",
                      "description": "The message Id of the email to retrieve"
                    },
                    "unreadOnly": {
                      "type": "boolean",
                      "description": "If true will only return unread emails. Default false."
                    },
                    "sender": {
                      "type": "string",
                      "description": "The email address of the person that sent the email you are looking for."
                    },
                    "subject": {
                      "type": "string",
                      "description": "The subject of the email you are looking for."
                    },
                    "to": {
                      "type": "string",
                      "description": "The To address of the email you are looking for."
                    },
                    "emailsSentAfter": {
                      "type": "string",
                      "description": "A Datetime parameter for the start date of emails to search for. This will return emails recieved after this datetime."
                    },
                    "emailsSentBefore": {
                      "type": "string",
                      "description": "A Datetime parameter for the end date of emails to search for. This will return emails recieved before this datetime."
                    },
                    "maxReturnedEmails": {
                      "type": "number",
                      "description": "The maximum number of emails to return at a time when searching for emails. The default is 10."
                    }
                  },
                  "required": []
                }
                """)]
    public async Task<object> GetEmailSummary(ServiceConfig config, ServiceRequest request)
    {
        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly);

            var searchQuery = BuildSearchQuery(request);
            var uids = await client.Inbox.SearchAsync(searchQuery);

            // Apply SearchSubject filter client-side (partial match)
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

                    summaries.Add(BuildSummary(message));
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

                    summaries.Add(BuildSummary(message));
                }
            }

            return new { Success = true, Total = emailIds.Any() ? emailIds.Count : client.Inbox.Count, Result = summaries };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }

        static object BuildSummary(MimeMessage msg)
        {
            var body = GetEmailBody(msg);
            return new
            {
                MessageId = msg.MessageId,
                Subject = msg.Subject,
                From = string.Join(", ", msg.From?.Select(s => s.Name) ?? new List<string>()),
                Date = msg.Date,
                Preview = GetPreview(body),
                Attachments = msg.Attachments?.Select(a => a is MimePart mp ? mp.FileName : a.ContentType?.Name).Where(n => n != null).ToList()
            };
        }
    }

    /// <summary>
    /// Retrieves all available labels associated with the email account.
    /// </summary>
    [Display(Name = "get_labels")]
    [Description("Use this tool to get a list of available email labels")]
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
    public async Task<object> GetLabels(ServiceConfig config, ServiceRequest request)
    {
        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            var labels = new List<string>();

            var personal = client.PersonalNamespaces[0];
            var folders = await client.GetFoldersAsync(personal);
            labels.AddRange(folders.Select(f => f.FullName));

            // Also enumerate [Gmail] subfolders
            try
            {
                var gmailFolder = await client.GetFolderAsync("[Gmail]");
                var gmailSubfolders = await gmailFolder.GetSubfoldersAsync();
                labels.AddRange(gmailSubfolders.Select(f => f.FullName));
            }
            catch { /* Gmail folder may not exist */ }

            return new { Success = true, Result = labels.Distinct().OrderBy(l => l).ToList() };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Retrieves the attachments associated with a specific email.
    /// </summary>
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

    #region Write Operations - No Confirmation (Group B)

    /// <summary>
    /// Marks the specified email as read/unread.
    /// </summary>
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
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            if (request.MarkAsRead == true)
                await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Seen, true);
            else
                await found.Value.folder.RemoveFlagsAsync(found.Value.uid, MessageFlags.Seen, true);

            var status = request.MarkAsRead == true ? "read" : "unread";
            return new { Success = true, Message = $"Email marked as {status}" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Stars/unstars the specified email.
    /// </summary>
    [Display(Name = "star")]
    [Description("Use this tool to star/unstar an email")]
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
                    "star": {
                      "type": "boolean",
                      "description": "Set to true to star the email, false to unstar"
                    }
                  },
                  "required": ["messageId","star"]
                }
                """)]
    public async Task<object> Star(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            if (request.Star == true)
                await found.Value.folder.AddFlagsAsync(found.Value.uid, MessageFlags.Flagged, true);
            else
                await found.Value.folder.RemoveFlagsAsync(found.Value.uid, MessageFlags.Flagged, true);

            var status = request.Star == true ? "starred" : "unstarred";
            return new { Success = true, Message = $"Email {status}" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Archives/Unarchives the specified email.
    /// </summary>
    [Display(Name = "archive")]
    [Description("Use this tool to archive/unarchive an email")]
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
                      "description": "The message Id of the email message to archive"
                    },
                    "archive": {
                      "type": "boolean",
                      "description": "Set to true to archive the email, false to unarchive"
                    }
                  },
                  "required": ["messageId","archive"]
                }
                """)]
    public async Task<object> Archive(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            if (request.Archive == true)
            {
                // Archive: move from Inbox to All Mail
                await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
                var found = await FindMessageAsync(client.Inbox, request.MessageId);
                if (found == null)
                    return new { Success = false, Message = "Email not found in Inbox" };

                var allMailFolder = await GetFolderAsync(client, "All Mail");
                await allMailFolder.OpenAsync(FolderAccess.ReadWrite);
                await found.Value.folder.MoveToAsync(found.Value.uid, allMailFolder);

                return new { Success = true, Message = "Email archived" };
            }
            else
            {
                // Unarchive: move from All Mail to Inbox
                var allMailFolder = await GetFolderAsync(client, "All Mail");
                await allMailFolder.OpenAsync(FolderAccess.ReadWrite);
                var found = await FindMessageAsync(allMailFolder, request.MessageId);
                if (found == null)
                    return new { Success = false, Message = "Email not found in All Mail" };

                await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
                await found.Value.folder.MoveToAsync(found.Value.uid, client.Inbox);

                return new { Success = true, Message = "Email unarchived" };
            }
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Moves a specific email to the folder specified in the request.
    /// </summary>
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
            await found.Value.folder.MoveToAsync(found.Value.uid, destFolder);

            return new { Success = true, Message = $"Email moved to {request.Folder}" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Adds a label to an email.
    /// </summary>
    [Display(Name = "add_label")]
    [Description("Use this tool to add a label to an email")]
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
                      "description": "The message Id of the email message to label"
                    },
                    "label": {
                      "type": "string",
                      "description": "The label you want to add to the email"
                    }
                  },
                  "required": ["messageId","label"]
                }
                """)]
    public async Task<object> AddLabel(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        if (string.IsNullOrWhiteSpace(request.Label))
            throw new Exception("Label is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            await found.Value.folder.AddLabelsAsync(found.Value.uid, new[] { request.Label }, true);

            return new { Success = true, Message = $"Label '{request.Label}' added" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    /// <summary>
    /// Removes a label from an email.
    /// </summary>
    [Display(Name = "remove_label")]
    [Description("Use this tool to remove a label from an email")]
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
                    "label": {
                      "type": "string",
                      "description": "The label you want to remove from the email"
                    }
                  },
                  "required": ["messageId","label"]
                }
                """)]
    public async Task<object> RemoveLabel(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        if (string.IsNullOrWhiteSpace(request.Label))
            throw new Exception("Label is required");

        var account = GetMailAccount(request, config);
        using var client = await ConnectImapAsync(account);
        try
        {
            await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
            var found = await FindMessageAsync(client.Inbox, request.MessageId);
            if (found == null)
                return new { Success = false, Message = "Email not found" };

            await found.Value.folder.RemoveLabelsAsync(found.Value.uid, new[] { request.Label }, true);

            return new { Success = true, Message = $"Label '{request.Label}' removed" };
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }

    #endregion

    #region Write Operations - With Confirmation (Group C)

    /// <summary>
    /// Sends an email using the supplied criteria.
    /// </summary>
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
    public async Task<object> SendEmail(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId) && string.IsNullOrWhiteSpace(request.To))
            throw new Exception("Must supply existing draft email Id or a To address to send an email.");

        // Confirmation flow
        if (string.IsNullOrWhiteSpace(request.ConfirmationId))
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

        if (_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
        {
            var account = GetMailAccount(request, config);

            MimeMessage message;
            if (!string.IsNullOrWhiteSpace(request.MessageId))
            {
                // Send existing draft
                using var imapClient = await ConnectImapAsync(account);
                try
                {
                    var draftsFolder = imapClient.GetFolder(SpecialFolder.Drafts);
                    await draftsFolder.OpenAsync(FolderAccess.ReadWrite);
                    var found = await FindMessageAsync(draftsFolder, request.MessageId);
                    if (found == null)
                        return new { Success = false, Message = "Draft not found" };

                    message = await found.Value.folder.GetMessageAsync(found.Value.uid);

                    // Delete the draft after retrieving it
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
                // Build new message
                message = new MimeMessage
                {
                    Subject = request.Subject,
                    Body = new TextPart("html") { Text = request.Body }
                };
                message.From.Add(new MailboxAddress(account.DisplayName, account.Email));
                message.To.Add(new MailboxAddress(request.RecipientName, request.To));
            }

            // Send via SMTP
            using var smtpClient = await ConnectSmtpAsync(account);
            try
            {
                await smtpClient.SendAsync(message);
                return await _notificationService.SendNotification("Email Sent!");
            }
            finally
            {
                await smtpClient.DisconnectAsync(true);
            }
        }

        return new { Success = false, Error = "Unable to send email without valid confirmation" };
    }

    /// <summary>
    /// Deletes the specified email by the supplied Message Id.
    /// </summary>
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
    public async Task<object> DeleteEmail(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MessageId))
            throw new Exception("MessageId is required");

        // Confirmation flow
        if (string.IsNullOrWhiteSpace(request.ConfirmationId))
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

        if (_notificationService.DoesConfirmationExist(Guid.Parse(request.ConfirmationId), out _))
        {
            var account = GetMailAccount(request, config);
            int deletedCount = 0;

            using var client = await ConnectImapAsync(account);
            try
            {
                await client.Inbox.OpenAsync(FolderAccess.ReadWrite);
                var uids = await client.Inbox.SearchAsync(SearchOptions.All, SearchQuery.HeaderContains("Message-Id", request.MessageId));

                foreach (var uid in uids.UniqueIds)
                {
                    await client.Inbox.AddFlagsAsync(uid, MessageFlags.Deleted, true);
                    deletedCount++;
                }

                await client.Inbox.ExpungeAsync();

                var message = deletedCount > 0 ? "Email(s) Deleted!" : "Unable to delete email(s)!";
                return await _notificationService.SendNotification(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }

        return new { Success = false, Error = "Unable to delete email without valid confirmation" };
    }

    #endregion

    #region Draft CRUD (Group D)

    /// <summary>
    /// Creates and saves a draft email.
    /// </summary>
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

    /// <summary>
    /// Removes a draft email by Message Id.
    /// </summary>
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

    /// <summary>
    /// Adds an attachment to a specified draft email.
    /// </summary>
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
                      "description": "The attachment to add to the draft email"
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

            // Parse attachment from request
            var attachmentJson = request.Attachment?.ToString();
            if (string.IsNullOrWhiteSpace(attachmentJson))
                return new { Success = false, Message = "Attachment data is required" };

            var attachmentData = JsonSerializer.Deserialize<JsonElement>(attachmentJson);
            var fileName = attachmentData.GetProperty("fileName").GetString();
            var contentType = attachmentData.GetProperty("contentType").GetString();
            var data = Convert.FromBase64String(attachmentData.GetProperty("data").GetString());

            // Rebuild body with existing content + new attachment
            var builder = new BodyBuilder();

            // Preserve existing body
            if (originalMessage.HtmlBody != null)
                builder.HtmlBody = originalMessage.HtmlBody;
            if (originalMessage.TextBody != null)
                builder.TextBody = originalMessage.TextBody;

            // Preserve existing attachments
            foreach (var existingAttachment in originalMessage.Attachments)
            {
                if (existingAttachment is MimePart mp)
                {
                    using var stream = new MemoryStream();
                    await mp.Content.DecodeToAsync(stream);
                    builder.Attachments.Add(mp.FileName, stream.ToArray(), ContentType.Parse(mp.ContentType.MimeType));
                }
            }

            // Add new attachment
            builder.Attachments.Add(fileName, data, ContentType.Parse(contentType));

            // Create new draft message (IMAP messages are immutable)
            var newMessage = new MimeMessage();
            newMessage.From.AddRange(originalMessage.From);
            newMessage.To.AddRange(originalMessage.To);
            newMessage.Cc.AddRange(originalMessage.Cc);
            newMessage.Bcc.AddRange(originalMessage.Bcc);
            newMessage.Subject = originalMessage.Subject;
            newMessage.Body = builder.ToMessageBody();

            // Delete old draft and append new one
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

    /// <summary>
    /// Removes an attachment from the specified draft email.
    /// </summary>
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

            // Rebuild body without the specified attachment
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
                    // Skip the attachment to remove (match by filename or content-id)
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

            // Create new draft message
            var newMessage = new MimeMessage();
            newMessage.From.AddRange(originalMessage.From);
            newMessage.To.AddRange(originalMessage.To);
            newMessage.Cc.AddRange(originalMessage.Cc);
            newMessage.Bcc.AddRange(originalMessage.Bcc);
            newMessage.Subject = originalMessage.Subject;
            newMessage.Body = builder.ToMessageBody();

            // Delete old, append new
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
