using HQ.Plugins.Email.Models;
using Microsoft.Data.Sqlite;

namespace HQ.Plugins.Email.Data;

public class LocalEmailStore : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SqliteConnection _connection;

    public LocalEmailStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentNullException(nameof(connectionString));

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        InitializeDatabase();
    }

    private SqliteConnection GetConnection() => _connection;

    private void InitializeDatabase()
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();

        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_name TEXT NOT NULL,
                folder TEXT NOT NULL,
                uid INTEGER NOT NULL,
                message_id TEXT,
                subject TEXT,
                from_address TEXT,
                from_name TEXT,
                to_address TEXT,
                cc_address TEXT,
                bcc_address TEXT,
                reply_to TEXT,
                date_sent TEXT,
                body_text TEXT,
                body_html TEXT,
                is_read INTEGER NOT NULL DEFAULT 0,
                is_flagged INTEGER NOT NULL DEFAULT 0,
                has_attachments INTEGER NOT NULL DEFAULT 0,
                attachment_names TEXT,
                vector_id TEXT,
                synced_at TEXT NOT NULL,
                UNIQUE(account_name, folder, uid)
            );

            CREATE INDEX IF NOT EXISTS idx_emails_message_id ON emails(message_id);
            CREATE INDEX IF NOT EXISTS idx_emails_account_folder ON emails(account_name, folder);
            CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date_sent);

            CREATE TABLE IF NOT EXISTS sync_state (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_name TEXT NOT NULL,
                folder TEXT NOT NULL,
                uid_validity INTEGER,
                last_synced_uid INTEGER NOT NULL DEFAULT 0,
                last_sync_time TEXT,
                UNIQUE(account_name, folder)
            );

            CREATE TABLE IF NOT EXISTS folders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account_name TEXT NOT NULL,
                folder_name TEXT NOT NULL,
                message_count INTEGER NOT NULL DEFAULT 0,
                unread_count INTEGER NOT NULL DEFAULT 0,
                special_use TEXT,
                last_updated TEXT,
                UNIQUE(account_name, folder_name)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<long> UpsertEmailAsync(LocalEmail email)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO emails (account_name, folder, uid, message_id, subject, from_address, from_name,
                    to_address, cc_address, bcc_address, reply_to, date_sent, body_text, body_html,
                    is_read, is_flagged, has_attachments, attachment_names, vector_id, synced_at)
                VALUES (@account, @folder, @uid, @messageId, @subject, @fromAddr, @fromName,
                    @toAddr, @ccAddr, @bccAddr, @replyTo, @dateSent, @bodyText, @bodyHtml,
                    @isRead, @isFlagged, @hasAttachments, @attachmentNames, @vectorId, @syncedAt)
                ON CONFLICT(account_name, folder, uid) DO UPDATE SET
                    message_id = @messageId, subject = @subject, from_address = @fromAddr, from_name = @fromName,
                    to_address = @toAddr, cc_address = @ccAddr, bcc_address = @bccAddr, reply_to = @replyTo,
                    date_sent = @dateSent, body_text = @bodyText, body_html = @bodyHtml,
                    is_read = @isRead, is_flagged = @isFlagged, has_attachments = @hasAttachments,
                    attachment_names = @attachmentNames, vector_id = @vectorId, synced_at = @syncedAt
                RETURNING id;
                """;

            cmd.Parameters.AddWithValue("@account", email.AccountName);
            cmd.Parameters.AddWithValue("@folder", email.Folder);
            cmd.Parameters.AddWithValue("@uid", (long)email.Uid);
            cmd.Parameters.AddWithValue("@messageId", (object)email.MessageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@subject", (object)email.Subject ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fromAddr", (object)email.FromAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@fromName", (object)email.FromName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@toAddr", (object)email.ToAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ccAddr", (object)email.CcAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bccAddr", (object)email.BccAddress ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@replyTo", (object)email.ReplyTo ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@dateSent", email.DateSent.ToString("O"));
            cmd.Parameters.AddWithValue("@bodyText", (object)email.BodyText ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@bodyHtml", (object)email.BodyHtml ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@isRead", email.IsRead ? 1 : 0);
            cmd.Parameters.AddWithValue("@isFlagged", email.IsFlagged ? 1 : 0);
            cmd.Parameters.AddWithValue("@hasAttachments", email.HasAttachments ? 1 : 0);
            cmd.Parameters.AddWithValue("@attachmentNames", (object)email.AttachmentNames ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@vectorId", (object)email.VectorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@syncedAt", email.SyncedAt.ToString("O"));

            var result = await cmd.ExecuteScalarAsync();
            return (long)result;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<LocalEmail> GetByMessageIdAsync(string messageId, string accountName = null)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = accountName != null
            ? "SELECT * FROM emails WHERE message_id = @messageId AND account_name = @account LIMIT 1"
            : "SELECT * FROM emails WHERE message_id = @messageId LIMIT 1";
        cmd.Parameters.AddWithValue("@messageId", messageId);
        if (accountName != null)
            cmd.Parameters.AddWithValue("@account", accountName);

        using var reader = await cmd.ExecuteReaderAsync();
        return reader.Read() ? MapEmail(reader) : null;
    }

    public async Task<List<LocalEmail>> SearchAsync(string accountName = null, string folder = null,
        string subject = null, string sender = null, string bodyText = null, string searchText = null,
        int maxResults = 50)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();

        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(accountName))
        {
            clauses.Add("account_name = @account");
            cmd.Parameters.AddWithValue("@account", accountName);
        }
        if (!string.IsNullOrWhiteSpace(folder))
        {
            clauses.Add("folder = @folder");
            cmd.Parameters.AddWithValue("@folder", folder);
        }
        if (!string.IsNullOrWhiteSpace(subject))
        {
            clauses.Add("subject LIKE @subject");
            cmd.Parameters.AddWithValue("@subject", $"%{subject}%");
        }
        if (!string.IsNullOrWhiteSpace(sender))
        {
            clauses.Add("(from_address LIKE @sender OR from_name LIKE @sender)");
            cmd.Parameters.AddWithValue("@sender", $"%{sender}%");
        }
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            clauses.Add("body_text LIKE @body");
            cmd.Parameters.AddWithValue("@body", $"%{bodyText}%");
        }
        if (!string.IsNullOrWhiteSpace(searchText))
        {
            clauses.Add("(subject LIKE @st OR from_address LIKE @st OR from_name LIKE @st OR body_text LIKE @st)");
            cmd.Parameters.AddWithValue("@st", $"%{searchText}%");
        }

        var where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
        cmd.CommandText = $"SELECT * FROM emails {where} ORDER BY date_sent DESC LIMIT @max";
        cmd.Parameters.AddWithValue("@max", maxResults);

        var results = new List<LocalEmail>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read())
            results.Add(MapEmail(reader));
        return results;
    }

    public async Task<List<LocalEmail>> GetByFolderAsync(string accountName, string folder, int maxResults = 50)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM emails WHERE account_name = @account AND folder = @folder ORDER BY date_sent DESC LIMIT @max";
        cmd.Parameters.AddWithValue("@account", accountName);
        cmd.Parameters.AddWithValue("@folder", folder);
        cmd.Parameters.AddWithValue("@max", maxResults);

        var results = new List<LocalEmail>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read())
            results.Add(MapEmail(reader));
        return results;
    }

    public async Task<List<uint>> GetUidsForFolderAsync(string accountName, string folder)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT uid FROM emails WHERE account_name = @account AND folder = @folder";
        cmd.Parameters.AddWithValue("@account", accountName);
        cmd.Parameters.AddWithValue("@folder", folder);

        var uids = new List<uint>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read())
            uids.Add((uint)(long)reader["uid"]);
        return uids;
    }

    public async Task<int> DeleteByUidsAsync(string accountName, string folder, IEnumerable<uint> uids)
    {
        var uidList = uids.ToList();
        if (uidList.Count == 0) return 0;

        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var placeholders = string.Join(",", uidList.Select((_, i) => $"@uid{i}"));
            cmd.CommandText = $"DELETE FROM emails WHERE account_name = @account AND folder = @folder AND uid IN ({placeholders})";
            cmd.Parameters.AddWithValue("@account", accountName);
            cmd.Parameters.AddWithValue("@folder", folder);
            for (int i = 0; i < uidList.Count; i++)
                cmd.Parameters.AddWithValue($"@uid{i}", (long)uidList[i]);

            return await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<string>> GetVectorIdsForUidsAsync(string accountName, string folder, IEnumerable<uint> uids)
    {
        var uidList = uids.ToList();
        if (uidList.Count == 0) return new List<string>();

        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        var placeholders = string.Join(",", uidList.Select((_, i) => $"@uid{i}"));
        cmd.CommandText = $"SELECT vector_id FROM emails WHERE account_name = @account AND folder = @folder AND uid IN ({placeholders}) AND vector_id IS NOT NULL";
        cmd.Parameters.AddWithValue("@account", accountName);
        cmd.Parameters.AddWithValue("@folder", folder);
        for (int i = 0; i < uidList.Count; i++)
            cmd.Parameters.AddWithValue($"@uid{i}", (long)uidList[i]);

        var ids = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    public async Task DeleteByMessageIdAsync(string messageId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM emails WHERE message_id = @messageId";
            cmd.Parameters.AddWithValue("@messageId", messageId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateFlagsAsync(string messageId, bool? isRead = null, bool? isFlagged = null)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            var sets = new List<string>();
            if (isRead.HasValue)
            {
                sets.Add("is_read = @isRead");
                cmd.Parameters.AddWithValue("@isRead", isRead.Value ? 1 : 0);
            }
            if (isFlagged.HasValue)
            {
                sets.Add("is_flagged = @isFlagged");
                cmd.Parameters.AddWithValue("@isFlagged", isFlagged.Value ? 1 : 0);
            }
            if (sets.Count == 0) return;

            cmd.CommandText = $"UPDATE emails SET {string.Join(", ", sets)} WHERE message_id = @messageId";
            cmd.Parameters.AddWithValue("@messageId", messageId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateFolderAsync(string messageId, string newFolder, uint newUid)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE emails SET folder = @folder, uid = @uid WHERE message_id = @messageId";
            cmd.Parameters.AddWithValue("@folder", newFolder);
            cmd.Parameters.AddWithValue("@uid", (long)newUid);
            cmd.Parameters.AddWithValue("@messageId", messageId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateVectorIdAsync(string messageId, string vectorId)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE emails SET vector_id = @vectorId WHERE message_id = @messageId";
            cmd.Parameters.AddWithValue("@vectorId", (object)vectorId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@messageId", messageId);
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #region Sync State

    public async Task<(uint uidValidity, uint lastSyncedUid, DateTime? lastSyncTime)?> GetSyncStateAsync(string accountName, string folder)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT uid_validity, last_synced_uid, last_sync_time FROM sync_state WHERE account_name = @account AND folder = @folder";
        cmd.Parameters.AddWithValue("@account", accountName);
        cmd.Parameters.AddWithValue("@folder", folder);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!reader.Read()) return null;

        var uidValidity = (uint)(long)reader["uid_validity"];
        var lastUid = (uint)(long)reader["last_synced_uid"];
        var lastTime = reader["last_sync_time"] is string s ? DateTime.Parse(s) : (DateTime?)null;
        return (uidValidity, lastUid, lastTime);
    }

    public async Task UpsertSyncStateAsync(string accountName, string folder, uint uidValidity, uint lastSyncedUid)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sync_state (account_name, folder, uid_validity, last_synced_uid, last_sync_time)
                VALUES (@account, @folder, @uidValidity, @lastUid, @syncTime)
                ON CONFLICT(account_name, folder) DO UPDATE SET
                    uid_validity = @uidValidity, last_synced_uid = @lastUid, last_sync_time = @syncTime;
                """;
            cmd.Parameters.AddWithValue("@account", accountName);
            cmd.Parameters.AddWithValue("@folder", folder);
            cmd.Parameters.AddWithValue("@uidValidity", (long)uidValidity);
            cmd.Parameters.AddWithValue("@lastUid", (long)lastSyncedUid);
            cmd.Parameters.AddWithValue("@syncTime", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task ResetSyncStateAsync(string accountName, string folder)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM emails WHERE account_name = @account AND folder = @folder";
            cmd.Parameters.AddWithValue("@account", accountName);
            cmd.Parameters.AddWithValue("@folder", folder);
            await cmd.ExecuteNonQueryAsync();

            cmd.CommandText = "DELETE FROM sync_state WHERE account_name = @account AND folder = @folder";
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    #endregion

    #region Folders

    public async Task UpsertFolderAsync(string accountName, string folderName, int messageCount, int unreadCount, string specialUse = null)
    {
        await _writeLock.WaitAsync();
        try
        {
            var conn = GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO folders (account_name, folder_name, message_count, unread_count, special_use, last_updated)
                VALUES (@account, @folder, @msgCount, @unreadCount, @specialUse, @updated)
                ON CONFLICT(account_name, folder_name) DO UPDATE SET
                    message_count = @msgCount, unread_count = @unreadCount, special_use = @specialUse, last_updated = @updated;
                """;
            cmd.Parameters.AddWithValue("@account", accountName);
            cmd.Parameters.AddWithValue("@folder", folderName);
            cmd.Parameters.AddWithValue("@msgCount", messageCount);
            cmd.Parameters.AddWithValue("@unreadCount", unreadCount);
            cmd.Parameters.AddWithValue("@specialUse", (object)specialUse ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@updated", DateTime.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<(string FolderName, int MessageCount, int UnreadCount, string SpecialUse)>> GetFoldersAsync(string accountName)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT folder_name, message_count, unread_count, special_use FROM folders WHERE account_name = @account ORDER BY folder_name";
        cmd.Parameters.AddWithValue("@account", accountName);

        var results = new List<(string, int, int, string)>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (reader.Read())
        {
            results.Add((
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)
            ));
        }
        return results;
    }

    public async Task<int> GetTotalEmailCountAsync(string accountName = null)
    {
        var conn = GetConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = accountName != null
            ? "SELECT COUNT(*) FROM emails WHERE account_name = @account"
            : "SELECT COUNT(*) FROM emails";
        if (accountName != null)
            cmd.Parameters.AddWithValue("@account", accountName);

        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    #endregion

    private static LocalEmail MapEmail(SqliteDataReader reader)
    {
        return new LocalEmail
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            AccountName = reader.GetString(reader.GetOrdinal("account_name")),
            Folder = reader.GetString(reader.GetOrdinal("folder")),
            Uid = (uint)reader.GetInt64(reader.GetOrdinal("uid")),
            MessageId = reader.IsDBNull(reader.GetOrdinal("message_id")) ? null : reader.GetString(reader.GetOrdinal("message_id")),
            Subject = reader.IsDBNull(reader.GetOrdinal("subject")) ? null : reader.GetString(reader.GetOrdinal("subject")),
            FromAddress = reader.IsDBNull(reader.GetOrdinal("from_address")) ? null : reader.GetString(reader.GetOrdinal("from_address")),
            FromName = reader.IsDBNull(reader.GetOrdinal("from_name")) ? null : reader.GetString(reader.GetOrdinal("from_name")),
            ToAddress = reader.IsDBNull(reader.GetOrdinal("to_address")) ? null : reader.GetString(reader.GetOrdinal("to_address")),
            CcAddress = reader.IsDBNull(reader.GetOrdinal("cc_address")) ? null : reader.GetString(reader.GetOrdinal("cc_address")),
            BccAddress = reader.IsDBNull(reader.GetOrdinal("bcc_address")) ? null : reader.GetString(reader.GetOrdinal("bcc_address")),
            ReplyTo = reader.IsDBNull(reader.GetOrdinal("reply_to")) ? null : reader.GetString(reader.GetOrdinal("reply_to")),
            DateSent = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("date_sent"))),
            BodyText = reader.IsDBNull(reader.GetOrdinal("body_text")) ? null : reader.GetString(reader.GetOrdinal("body_text")),
            BodyHtml = reader.IsDBNull(reader.GetOrdinal("body_html")) ? null : reader.GetString(reader.GetOrdinal("body_html")),
            IsRead = reader.GetInt64(reader.GetOrdinal("is_read")) == 1,
            IsFlagged = reader.GetInt64(reader.GetOrdinal("is_flagged")) == 1,
            HasAttachments = reader.GetInt64(reader.GetOrdinal("has_attachments")) == 1,
            AttachmentNames = reader.IsDBNull(reader.GetOrdinal("attachment_names")) ? null : reader.GetString(reader.GetOrdinal("attachment_names")),
            VectorId = reader.IsDBNull(reader.GetOrdinal("vector_id")) ? null : reader.GetString(reader.GetOrdinal("vector_id")),
            SyncedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("synced_at")))
        };
    }

    public void Dispose()
    {
        _connection.Dispose();
        _writeLock.Dispose();
    }
}
