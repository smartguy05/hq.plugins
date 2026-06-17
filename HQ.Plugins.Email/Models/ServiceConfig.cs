using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Email.Models;

public record ServiceConfig: IPluginConfig
{
    [Hidden]
    public string Name { get; set; }
    [Hidden]
    public string Description { get; set; }

    [Tooltip("List of email accounts this plugin can send and receive from")]
    public IEnumerable<EmailParameters> EmailAccounts { get; set; }

    [Hidden]
    public string SqliteConnectionString { get; set; }

    // System-managed: the internal Chroma URL is injected by the host at runtime and
    // must not be shown or editable in the config UI.
    [Hidden]
    public string ChromaUrl { get; set; }

    // System-managed: the collection name is auto-derived per-agent (agent-{id}-emails)
    // by EmailVectorService and is never shown or editable.
    [Hidden]
    public string ChromaCollectionName { get; set; }

    [Sensitive]
    [LlmProviderKey]
    public string OpenAiApiKey { get; set; }

    [LlmProviderUrl]
    public string OpenAiUrl { get; set; }

    [LlmProviderModel("embedding")]
    public string EmbeddingModel { get; set; }

    [Hidden]
    [Tooltip("Agent ID that owns this config. Auto-populated by the system.")]
    public string AgentId { get; set; }

    [Tooltip("Whether to require user confirmation before sending emails")]
    public bool RequiresConfirmation { get; set; } = true;

    [Tooltip("How often to sync email from the server, in minutes")]
    public int SyncIntervalMinutes { get; set; } = 15;

    [Tooltip("IMAP folders to sync. Default: all standard folders (Inbox, Sent, Drafts, Trash, Junk, Archive). Use '*' to sync all folders.")]
    public IEnumerable<string> SyncFolders { get; set; }

    [Tooltip("Maximum number of characters to index per email body. Longer emails are truncated.")]
    public int MaxEmailBodyChars { get; set; } = 50000;

    [Tooltip("Whether to extract and index text from email attachments (PDF, DOCX, TXT)")]
    public bool IndexTextAttachments { get; set; }

    [Tooltip("Email addresses or domain wildcards (@example.com) always treated as trusted — " +
             "their content is not wrapped for prompt-injection scanning. The agent cannot remove " +
             "these. Compared case-insensitively.")]
    public IEnumerable<string> TrustedSenderSeed { get; set; } = Array.Empty<string>();
}
