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

    [Tooltip("ChromaDB server URL for email embedding storage, e.g. http://127.0.0.1:8000")]
    public string ChromaUrl { get; set; }

    [Tooltip("ChromaDB collection name for this agent's email embeddings")]
    public string ChromaCollectionName { get; set; }

    [Sensitive]
    [LlmProviderKey]
    public string OpenAiApiKey { get; set; }

    [LlmProviderUrl]
    public string OpenAiUrl { get; set; }

    [LlmProviderModel]
    public string EmbeddingModel { get; set; }

    [Hidden]
    [Tooltip("Agent ID that owns this config. Auto-populated by the system.")]
    public string AgentId { get; set; }

    [Tooltip("Whether to require user confirmation before sending emails")]
    public bool RequiresConfirmation { get; set; } = true;

    [Tooltip("How often to sync email from the server, in minutes")]
    public int SyncIntervalMinutes { get; set; } = 15;

    [Tooltip("IMAP folders to sync, e.g. INBOX, Sent")]
    public IEnumerable<string> SyncFolders { get; set; }

    [Tooltip("Maximum number of characters to index per email body. Longer emails are truncated.")]
    public int MaxEmailBodyChars { get; set; } = 50000;

    [Tooltip("Whether to extract and index text from email attachments (PDF, DOCX, TXT)")]
    public bool IndexTextAttachments { get; set; }
}
