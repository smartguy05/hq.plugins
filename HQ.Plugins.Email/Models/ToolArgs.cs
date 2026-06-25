using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Email.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable). Confirmation tools implement
/// <see cref="IPluginServiceRequest"/> so the request survives the confirmation replay round-trip.
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

public class GetEmailArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email to retrieve")]
    public string MessageId { get; set; }
}

public class GetDraftsArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    /// <summary>Not advertised to the model (kept at the historical default of 10).</summary>
    [Injected]
    public int MaxReturnedEmails { get; set; } = 10;
}

public class GetEmailSummaryArgs
{
    [Description("The email account to access. Optional — uses the default account if not specified.")]
    public string Account { get; set; }

    [Description("Client-side partial match on subject line (case-insensitive). Applied after IMAP results are fetched. Use 'subject' for server-side IMAP filtering instead when possible.")]
    public string SearchSubject { get; set; }

    [Description("The exact Message-Id header to retrieve a specific email.")]
    public string MessageId { get; set; }

    [Description("If true, only return unread/unseen emails. Default false.")]
    public bool UnreadOnly { get; set; }

    [Description("Filter by sender (From field). Use this to find emails FROM a specific person — pass their email address or name, e.g. 'john@example.com' or 'John'. This is the correct parameter for person-based searches.")]
    public string Sender { get; set; }

    [Description("IMAP server-side subject search (partial match). Use this to find emails about a specific topic.")]
    public string Subject { get; set; }

    [Description("Filter by To address. Use this to find emails sent TO a specific person.")]
    public string To { get; set; }

    [Description("ISO datetime string. Only return emails received after this date, e.g. '2025-01-15'.")]
    public string EmailsSentAfter { get; set; }

    [Description("ISO datetime string. Only return emails received before this date, e.g. '2025-02-01'.")]
    public string EmailsSentBefore { get; set; }

    [Description("Maximum number of emails to return. Default 10.")]
    public int MaxReturnedEmails { get; set; } = 10;
}

public class GetAttachmentsArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email to get attachments for")]
    public string MessageId { get; set; }
}

public class SearchEmailsArgs
{
    [Required, Description("Natural language search query, e.g. 'emails about the project deadline' or 'messages from John about invoices'")]
    public string Query { get; set; }

    [Description("Maximum number of results to return. Default 10.")]
    public int? MaxResults { get; set; }
}

public class SearchEmailsLocalArgs
{
    [Description("The email account to search. Optional, searches all accounts if not specified.")]
    public string Account { get; set; }

    [Description("General search text — matches across subject, sender (address and name), and body using OR logic. Use this for broad searches like a person's name or a keyword. Can be combined with other filters (folder, sender, subject) which are AND-ed.")]
    public string SearchText { get; set; }

    [Description("Limit search to a specific folder (e.g. 'INBOX', 'Sent'). Optional.")]
    public string Folder { get; set; }

    [Description("Filter by sender email address or display name (partial match). Use this to find emails FROM a specific person, e.g. 'anthony@apetalo.us' or 'Anthony'. This is the correct parameter for person-based searches.")]
    public string Sender { get; set; }

    [Description("Filter by subject line text (partial match). Overrides searchText for subject matching when both are provided.")]
    public string Subject { get; set; }

    [Description("Maximum number of results to return. Default 20.")]
    public int? MaxResults { get; set; }
}

public class AddTrustedSenderArgs
{
    [Required, Description("Email address or '@domain' wildcard to trust. Case-insensitive. Exact match for addresses; any sender from the domain for wildcards.")]
    public string Sender { get; set; }

    [Required, Description("Short explanation of why this sender is being trusted. Stored for audit.")]
    public string Reason { get; set; }
}

public class RemoveTrustedSenderArgs
{
    [Required, Description("Email address or '@domain' wildcard to remove from the whitelist.")]
    public string Sender { get; set; }
}

public class SyncEmailsArgs
{
    [Description("Specific account to sync. Optional, syncs all accounts if not specified.")]
    public string Account { get; set; }
}

public class GetFoldersArgs
{
    [Description("The email account. Optional, uses default account.")]
    public string Account { get; set; }
}

public class MarkAsReadArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email message to update")]
    public string MessageId { get; set; }

    [Required, Description("True the email with be marked as Read, False the email will be marked as Unread.")]
    public bool? MarkAsRead { get; set; }

    /// <summary>Not advertised; SetSeenFlag uses the default (inbox) folder.</summary>
    [Injected]
    public string Folder { get; set; }
}

public class FlagEmailArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email message to update")]
    public string MessageId { get; set; }

    [Required, Description("Set to true to flag the email, false to unflag")]
    public bool? Flag { get; set; }

    [Injected]
    public string Folder { get; set; }
}

public class MoveToFolderArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email message to move")]
    public string MessageId { get; set; }

    [Required, Description("The folder you wish to move the email to")]
    public string Folder { get; set; }
}

public class SendEmailArgs : IPluginServiceRequest
{
    // Framework envelope fields — supplied by the orchestrator, hidden from the LLM schema,
    // preserved across the confirmation replay.
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Description("The subject of the email to send.")]
    public string Subject { get; set; }

    [Description("The HTML formatted body of the email to send. This should be well-structured and styled using HTML tags like <p>, <h1>, <strong>, <ul>, <ol>, etc.")]
    public string Body { get; set; }

    [Description("The name of the person receiving the email.")]
    public string RecipientName { get; set; }

    [Description("The email address to send the email to.")]
    public string To { get; set; }

    [Description("The message Id of the draft email to send. Use this if you have already created an draft and want to send it.")]
    public string MessageId { get; set; }
}

public class DeleteEmailArgs : IPluginServiceRequest
{
    [Injected] public string Method { get; set; }
    [Injected] public string ToolCallId { get; set; }
    [Injected] public string RequestingService { get; set; }
    [Injected] public string ConfirmationId { get; set; }

    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email message to delete")]
    public string MessageId { get; set; }

    /// <summary>Used only for the confirmation prompt content; not advertised.</summary>
    [Injected] public string Body { get; set; }

    [Injected] public string Folder { get; set; }
}

public class CreateDraftArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Description("The subject of the email to send.")]
    public string Subject { get; set; }

    [Description("The HTML formatted body of the email to send. This should be well-structured and styled using HTML tags like <p>, <h1>, <strong>, <ul>, <ol>, etc.")]
    public string Body { get; set; }

    [Description("The name of the person receiving the email.")]
    public string RecipientName { get; set; }

    [Description("The email address to send the email to.")]
    public string To { get; set; }
}

public class DeleteDraftArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the draft email to delete")]
    public string MessageId { get; set; }
}

/// <summary>Nested attachment payload for add_attachment_to_draft.</summary>
public class AttachmentInput
{
    [Required, Description("The name of the file (e.g. report.pdf)")]
    public string FileName { get; set; }

    [Description("MIME type (e.g. application/pdf, text/plain). Defaults to application/octet-stream if not provided.")]
    public string ContentType { get; set; }

    [Required, Description("Base64-encoded file content")]
    public string Data { get; set; }
}

public class AddAttachmentToDraftArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email to add the attachment to")]
    public string MessageId { get; set; }

    [Description("The attachment to add to the draft email")]
    public AttachmentInput Attachment { get; set; }
}

public class RemoveAttachmentFromDraftArgs
{
    [Description("The email account you want to access. This is optional, if not supplied the default account will be used.")]
    public string Account { get; set; }

    [Required, Description("The message Id of the email to remove the attachment from")]
    public string MessageId { get; set; }

    [Description("The id or filename of the attachment to remove from the draft email.")]
    public string Attachment { get; set; }
}
