using System.Text.RegularExpressions;
using System.Web;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Email.Models;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;

namespace HQ.Plugins.Email.Data;

public class EmailSyncEngine : IDisposable
{
    private readonly LocalEmailStore _store;
    private readonly EmailVectorService _vectorService;
    private readonly ServiceConfig _config;
    private readonly LogDelegate _logger;
    private CancellationTokenSource _cts;
    private Task _backgroundTask;

    public bool IsRunning => _backgroundTask is { IsCompleted: false };
    public DateTime? LastSyncTime { get; private set; }

    public EmailSyncEngine(LocalEmailStore store, EmailVectorService vectorService, ServiceConfig config, LogDelegate logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vectorService = vectorService; // can be null if ChromaDB not configured
    }

    public void StartBackground()
    {
        if (IsRunning) return;
        var interval = _config.SyncIntervalMinutes > 0 ? _config.SyncIntervalMinutes : 15;
        _cts = new CancellationTokenSource();

        _backgroundTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(interval));
            try
            {
                // Run once immediately
                await SyncAllAccountsAsync(_cts.Token);

                while (await timer.WaitForNextTickAsync(_cts.Token))
                {
                    await SyncAllAccountsAsync(_cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await _logger(LogLevel.Error, $"Background sync error: {ex.Message}");
            }
        }, _cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    public async Task<object> SyncAllAccountsAsync(CancellationToken ct = default)
    {
        var accountResults = new List<object>();

        foreach (var account in _config.EmailAccounts ?? Enumerable.Empty<EmailParameters>())
        {
            try
            {
                var result = await SyncAccountAsync(account, ct);
                accountResults.Add(result);
            }
            catch (Exception ex)
            {
                await _logger(LogLevel.Error, $"Sync error for {account.Name}: {ex.Message}");
                accountResults.Add(new { Account = account.Name, Error = ex.Message });
            }
        }

        LastSyncTime = DateTime.UtcNow;
        return new { Success = true, Accounts = accountResults, SyncedAt = LastSyncTime };
    }

    private async Task<object> SyncAccountAsync(EmailParameters account, CancellationToken ct)
    {
        using var client = new ImapClient();
        await client.ConnectAsync(account.Imap, account.ImapPort, account.UseSsl, ct);
        await client.AuthenticateAsync(account.Username, account.Password, ct);

        int totalNew = 0, totalDeleted = 0;
        var syncedFolders = new List<string>();

        try
        {
            var folders = await DiscoverFoldersAsync(client, account.Name, ct);

            foreach (var folder in folders)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await folder.OpenAsync(FolderAccess.ReadOnly, ct);
                    var (added, deleted) = await SyncFolderAsync(account.Name, folder, ct);
                    totalNew += added;
                    totalDeleted += deleted;
                    syncedFolders.Add(folder.FullName);

                    await _store.UpsertFolderAsync(account.Name, folder.FullName, folder.Count, folder.Unread,
                        GetSpecialUse(folder));
                }
                catch (Exception ex)
                {
                    await _logger(LogLevel.Warning, $"Error syncing folder {folder.FullName}: {ex.Message}");
                }
            }
        }
        finally
        {
            await client.DisconnectAsync(true, ct);
        }

        return new { Account = account.Name, NewEmails = totalNew, DeletedEmails = totalDeleted, Folders = syncedFolders };
    }

    private async Task<List<IMailFolder>> DiscoverFoldersAsync(ImapClient client, string accountName, CancellationToken ct)
    {
        var syncFolders = _config.SyncFolders?.ToList();
        var personal = client.PersonalNamespaces[0];
        var allFolders = (await client.GetFoldersAsync(personal, cancellationToken: ct)).ToList();

        if (syncFolders == null || syncFolders.Count == 0)
        {
            // Default: INBOX only
            return [client.Inbox];
        }

        if (syncFolders.Contains("*"))
        {
            // All folders
            return allFolders.Where(f => f.Exists).ToList();
        }

        // Explicit folder list
        var result = new List<IMailFolder>();
        foreach (var name in syncFolders)
        {
            var folder = allFolders.FirstOrDefault(f =>
                string.Equals(f.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (folder != null) result.Add(folder);
        }
        return result;
    }

    private async Task<(int added, int deleted)> SyncFolderAsync(string accountName, IMailFolder folder, CancellationToken ct)
    {
        var folderName = folder.FullName;
        var serverUidValidity = folder.UidValidity;

        // Check UIDVALIDITY
        var syncState = await _store.GetSyncStateAsync(accountName, folderName);
        if (syncState.HasValue && syncState.Value.uidValidity != serverUidValidity)
        {
            await _logger(LogLevel.Warning, $"UIDVALIDITY changed for {folderName}, resetting sync state");
            // Get vector IDs before deleting
            var allLocalUids = await _store.GetUidsForFolderAsync(accountName, folderName);
            if (_vectorService != null)
            {
                var vectorIds = await _store.GetVectorIdsForUidsAsync(accountName, folderName, allLocalUids);
                await _vectorService.DeleteByVectorIdsAsync(vectorIds);
            }
            await _store.ResetSyncStateAsync(accountName, folderName);
            syncState = null;
        }

        var lastSyncedUid = syncState?.lastSyncedUid ?? 0;

        // Incremental sync: fetch new messages
        int added = 0;
        if (folder.Count > 0)
        {
            var searchRange = new UniqueIdRange(new UniqueId(lastSyncedUid + 1), UniqueId.MaxValue);
            var newUids = await folder.SearchAsync(SearchQuery.Uids(searchRange), ct);

            var batch = new List<UniqueId>();
            foreach (var uid in newUids)
            {
                batch.Add(uid);
                if (batch.Count >= 50)
                {
                    added += await ProcessBatchAsync(accountName, folder, batch, ct);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
                added += await ProcessBatchAsync(accountName, folder, batch, ct);

            if (newUids.Count > 0)
            {
                var maxUid = newUids.Max(u => u.Id);
                await _store.UpsertSyncStateAsync(accountName, folderName, serverUidValidity, maxUid);
            }
        }

        // Deletion detection
        int deleted = 0;
        var localUids = await _store.GetUidsForFolderAsync(accountName, folderName);
        if (localUids.Count > 0)
        {
            var serverUids = await folder.SearchAsync(SearchQuery.All, ct);
            var serverUidSet = new HashSet<uint>(serverUids.Select(u => u.Id));
            var deletedUids = localUids.Where(u => !serverUidSet.Contains(u)).ToList();

            if (deletedUids.Count > 0)
            {
                if (_vectorService != null)
                {
                    var vectorIds = await _store.GetVectorIdsForUidsAsync(accountName, folderName, deletedUids);
                    await _vectorService.DeleteByVectorIdsAsync(vectorIds);
                }
                deleted = await _store.DeleteByUidsAsync(accountName, folderName, deletedUids);
            }
        }

        // Update sync state even if no new messages
        if (lastSyncedUid > 0 || added > 0)
            await _store.UpsertSyncStateAsync(accountName, folderName, serverUidValidity, lastSyncedUid);

        return (added, deleted);
    }

    private async Task<int> ProcessBatchAsync(string accountName, IMailFolder folder, List<UniqueId> uids, CancellationToken ct)
    {
        int count = 0;
        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var message = await folder.GetMessageAsync(uid, ct);
                var summary = await folder.FetchAsync(new[] { uid }, MessageSummaryItems.Flags, ct);
                var flags = summary.FirstOrDefault()?.Flags ?? MessageFlags.None;

                var localEmail = MapToLocalEmail(accountName, folder.FullName, uid.Id, message, flags);
                await _store.UpsertEmailAsync(localEmail);

                // Index in vector store
                if (_vectorService != null)
                {
                    try
                    {
                        var vectorId = await _vectorService.IndexEmailAsync(localEmail);
                        if (vectorId != null)
                            await _store.UpdateVectorIdAsync(localEmail.MessageId, vectorId);
                    }
                    catch (Exception ex)
                    {
                        await _logger(LogLevel.Warning, $"Failed to index email {localEmail.MessageId}: {ex.Message}");
                    }
                }

                count++;
            }
            catch (Exception ex)
            {
                await _logger(LogLevel.Warning, $"Failed to sync UID {uid.Id}: {ex.Message}");
            }
        }
        return count;
    }

    private static LocalEmail MapToLocalEmail(string accountName, string folderName, uint uid, MimeMessage message, MessageFlags flags)
    {
        return new LocalEmail
        {
            AccountName = accountName,
            Folder = folderName,
            Uid = uid,
            MessageId = message.MessageId,
            Subject = message.Subject,
            FromAddress = message.From?.Mailboxes.FirstOrDefault()?.Address,
            FromName = message.From?.Mailboxes.FirstOrDefault()?.Name,
            ToAddress = string.Join(", ", message.To?.Mailboxes.Select(m => m.Address) ?? []),
            CcAddress = string.Join(", ", message.Cc?.Mailboxes.Select(m => m.Address) ?? []),
            BccAddress = string.Join(", ", message.Bcc?.Mailboxes.Select(m => m.Address) ?? []),
            ReplyTo = string.Join(", ", message.ReplyTo?.Mailboxes.Select(m => m.Address) ?? []),
            DateSent = message.Date,
            BodyText = GetPlainText(message),
            BodyHtml = message.HtmlBody,
            IsRead = flags.HasFlag(MessageFlags.Seen),
            IsFlagged = flags.HasFlag(MessageFlags.Flagged),
            HasAttachments = message.Attachments?.Any() == true,
            AttachmentNames = string.Join(", ", message.Attachments?.OfType<MimePart>().Select(a => a.FileName).Where(n => n != null) ?? []),
            SyncedAt = DateTime.UtcNow
        };
    }

    private static string GetPlainText(MimeMessage message)
    {
        if (message.TextBody != null)
            return message.TextBody;

        if (message.HtmlBody != null)
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(message.HtmlBody);
            var text = doc.DocumentNode.InnerText.Trim();
            text = HttpUtility.HtmlDecode(text);
            text = Regex.Replace(text, @"[\r\n\t]+", " ");
            text = Regex.Replace(text, @"\s+", " ");
            return text.Trim();
        }

        return string.Empty;
    }

    private static string GetSpecialUse(IMailFolder folder)
    {
        if (folder.Attributes.HasFlag(FolderAttributes.Inbox)) return "Inbox";
        if (folder.Attributes.HasFlag(FolderAttributes.Drafts)) return "Drafts";
        if (folder.Attributes.HasFlag(FolderAttributes.Sent)) return "Sent";
        if (folder.Attributes.HasFlag(FolderAttributes.Trash)) return "Trash";
        if (folder.Attributes.HasFlag(FolderAttributes.Junk)) return "Junk";
        if (folder.Attributes.HasFlag(FolderAttributes.Archive)) return "Archive";
        return null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
