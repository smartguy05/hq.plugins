using HQ.Models.Attributes;

namespace HQ.Plugins.Email.Models;

public record EmailParameters
{
    [Tooltip("Friendly name for this account, e.g. Work, Personal")]
    public string Name { get; set; }

    [Tooltip("Display name used in outgoing emails, e.g. John Smith")]
    public string DisplayName { get; set; }

    [Tooltip("Email address for this account, e.g. john@example.com")]
    public string Email { get; set; }

    [Tooltip("Whether this is the default account for sending")]
    public bool Default { get; set; }

    [Tooltip("IMAP server hostname, e.g. imap.gmail.com")]
    public string Imap { get; set; }

    [Tooltip("IMAP server port. Typically 993 for SSL or 143 for STARTTLS.")]
    public int ImapPort { get; set; }

    [Tooltip("SMTP server hostname, e.g. smtp.gmail.com")]
    public string Smtp { get; set; }

    [Tooltip("SMTP server port. Typically 587 for STARTTLS or 465 for SSL.")]
    public int SmtpPort { get; set; }

    [Tooltip("Login username, usually the full email address")]
    public string Username { get; set; }

    [Sensitive]
    [Tooltip("Account password or app-specific password")]
    public string Password { get; set; }

    [Tooltip("Whether to use SSL/TLS for IMAP and SMTP connections")]
    public bool UseSsl { get; set; }
}
