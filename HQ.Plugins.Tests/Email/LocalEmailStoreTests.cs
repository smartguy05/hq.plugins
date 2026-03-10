using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Tests.Email;

public class LocalEmailStoreTests : IDisposable
{
    private readonly LocalEmailStore _store;

    public LocalEmailStoreTests()
    {
        _store = new LocalEmailStore("Data Source=:memory:");
    }

    private static LocalEmail CreateTestEmail(string accountName = "test", string folder = "INBOX",
        uint uid = 1, string messageId = "msg1@test.com") => new()
    {
        AccountName = accountName,
        Folder = folder,
        Uid = uid,
        MessageId = messageId,
        Subject = "Test Subject",
        FromAddress = "sender@test.com",
        FromName = "Sender Name",
        ToAddress = "recipient@test.com",
        DateSent = DateTimeOffset.UtcNow,
        BodyText = "This is the email body text",
        IsRead = false,
        IsFlagged = false,
        HasAttachments = false,
        SyncedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task UpsertEmail_InsertsAndReturnsId()
    {
        var email = CreateTestEmail();
        var id = await _store.UpsertEmailAsync(email);
        Assert.True(id > 0);
    }

    [Fact]
    public async Task UpsertEmail_UpdatesOnConflict()
    {
        var email = CreateTestEmail();
        var id1 = await _store.UpsertEmailAsync(email);

        email.Subject = "Updated Subject";
        var id2 = await _store.UpsertEmailAsync(email);

        // Should be same row (upsert on unique constraint)
        var retrieved = await _store.GetByMessageIdAsync("msg1@test.com");
        Assert.Equal("Updated Subject", retrieved.Subject);
    }

    [Fact]
    public async Task GetByMessageId_ReturnsEmail()
    {
        var email = CreateTestEmail();
        await _store.UpsertEmailAsync(email);

        var result = await _store.GetByMessageIdAsync("msg1@test.com");
        Assert.NotNull(result);
        Assert.Equal("Test Subject", result.Subject);
        Assert.Equal("sender@test.com", result.FromAddress);
    }

    [Fact]
    public async Task GetByMessageId_ReturnsNullWhenNotFound()
    {
        var result = await _store.GetByMessageIdAsync("nonexistent@test.com");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByMessageId_FiltersOnAccount()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(accountName: "acc1", messageId: "msg@test.com", uid: 1));
        await _store.UpsertEmailAsync(CreateTestEmail(accountName: "acc2", messageId: "msg@test.com", uid: 2));

        var result = await _store.GetByMessageIdAsync("msg@test.com", "acc1");
        Assert.NotNull(result);
        Assert.Equal("acc1", result.AccountName);
    }

    [Fact]
    public async Task Search_BySubject()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(messageId: "m1@test.com", uid: 1));
        var e2 = CreateTestEmail(messageId: "m2@test.com", uid: 2);
        e2.Subject = "Important Meeting";
        await _store.UpsertEmailAsync(e2);

        var results = await _store.SearchAsync(subject: "Meeting");
        Assert.Single(results);
        Assert.Equal("Important Meeting", results[0].Subject);
    }

    [Fact]
    public async Task Search_BySender()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(messageId: "m1@test.com", uid: 1));
        var e2 = CreateTestEmail(messageId: "m2@test.com", uid: 2);
        e2.FromAddress = "boss@company.com";
        await _store.UpsertEmailAsync(e2);

        var results = await _store.SearchAsync(sender: "boss");
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_ByBodyText()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(messageId: "m1@test.com", uid: 1));
        var e2 = CreateTestEmail(messageId: "m2@test.com", uid: 2);
        e2.BodyText = "Please review the quarterly report";
        await _store.UpsertEmailAsync(e2);

        var results = await _store.SearchAsync(bodyText: "quarterly");
        Assert.Single(results);
    }

    [Fact]
    public async Task Search_RespectsMaxResults()
    {
        for (uint i = 1; i <= 10; i++)
            await _store.UpsertEmailAsync(CreateTestEmail(messageId: $"m{i}@test.com", uid: i));

        var results = await _store.SearchAsync(maxResults: 5);
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public async Task GetUidsForFolder_ReturnsCorrectUids()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(uid: 10, messageId: "m1@t.com"));
        await _store.UpsertEmailAsync(CreateTestEmail(uid: 20, messageId: "m2@t.com"));
        await _store.UpsertEmailAsync(CreateTestEmail(folder: "Sent", uid: 30, messageId: "m3@t.com"));

        var uids = await _store.GetUidsForFolderAsync("test", "INBOX");
        Assert.Equal(2, uids.Count);
        Assert.Contains(10u, uids);
        Assert.Contains(20u, uids);
    }

    [Fact]
    public async Task DeleteByUids_RemovesCorrectEmails()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(uid: 1, messageId: "m1@t.com"));
        await _store.UpsertEmailAsync(CreateTestEmail(uid: 2, messageId: "m2@t.com"));
        await _store.UpsertEmailAsync(CreateTestEmail(uid: 3, messageId: "m3@t.com"));

        var deleted = await _store.DeleteByUidsAsync("test", "INBOX", new uint[] { 1, 3 });
        Assert.Equal(2, deleted);

        var remaining = await _store.GetUidsForFolderAsync("test", "INBOX");
        Assert.Single(remaining);
        Assert.Contains(2u, remaining);
    }

    [Fact]
    public async Task UpdateFlags_UpdatesReadAndFlagged()
    {
        await _store.UpsertEmailAsync(CreateTestEmail());

        await _store.UpdateFlagsAsync("msg1@test.com", isRead: true, isFlagged: true);

        var result = await _store.GetByMessageIdAsync("msg1@test.com");
        Assert.True(result.IsRead);
        Assert.True(result.IsFlagged);
    }

    [Fact]
    public async Task UpdateFolder_UpdatesFolderAndUid()
    {
        await _store.UpsertEmailAsync(CreateTestEmail());

        await _store.UpdateFolderAsync("msg1@test.com", "Archive", 99);

        var result = await _store.GetByMessageIdAsync("msg1@test.com");
        Assert.Equal("Archive", result.Folder);
        Assert.Equal(99u, result.Uid);
    }

    [Fact]
    public async Task UpdateVectorId_SetsVectorId()
    {
        await _store.UpsertEmailAsync(CreateTestEmail());

        await _store.UpdateVectorIdAsync("msg1@test.com", "vec-123");

        var result = await _store.GetByMessageIdAsync("msg1@test.com");
        Assert.Equal("vec-123", result.VectorId);
    }

    [Fact]
    public async Task GetVectorIdsForUids_ReturnsOnlyNonNull()
    {
        var e1 = CreateTestEmail(uid: 1, messageId: "m1@t.com");
        e1.VectorId = "v1";
        await _store.UpsertEmailAsync(e1);

        var e2 = CreateTestEmail(uid: 2, messageId: "m2@t.com");
        await _store.UpsertEmailAsync(e2); // no vector_id

        var ids = await _store.GetVectorIdsForUidsAsync("test", "INBOX", new uint[] { 1, 2 });
        Assert.Single(ids);
        Assert.Equal("v1", ids[0]);
    }

    [Fact]
    public async Task SyncState_UpsertAndRetrieve()
    {
        await _store.UpsertSyncStateAsync("test", "INBOX", 12345, 100);

        var state = await _store.GetSyncStateAsync("test", "INBOX");
        Assert.NotNull(state);
        Assert.Equal(12345u, state.Value.uidValidity);
        Assert.Equal(100u, state.Value.lastSyncedUid);
        Assert.NotNull(state.Value.lastSyncTime);
    }

    [Fact]
    public async Task SyncState_ReturnsNullWhenNotFound()
    {
        var state = await _store.GetSyncStateAsync("test", "INBOX");
        Assert.Null(state);
    }

    [Fact]
    public async Task ResetSyncState_ClearsEmailsAndState()
    {
        await _store.UpsertEmailAsync(CreateTestEmail());
        await _store.UpsertSyncStateAsync("test", "INBOX", 123, 1);

        await _store.ResetSyncStateAsync("test", "INBOX");

        var emails = await _store.GetUidsForFolderAsync("test", "INBOX");
        Assert.Empty(emails);

        var state = await _store.GetSyncStateAsync("test", "INBOX");
        Assert.Null(state);
    }

    [Fact]
    public async Task Folders_UpsertAndRetrieve()
    {
        await _store.UpsertFolderAsync("test", "INBOX", 100, 5, "Inbox");
        await _store.UpsertFolderAsync("test", "Sent", 50, 0, "Sent");

        var folders = await _store.GetFoldersAsync("test");
        Assert.Equal(2, folders.Count);
        Assert.Contains(folders, f => f.FolderName == "INBOX" && f.MessageCount == 100);
        Assert.Contains(folders, f => f.FolderName == "Sent" && f.MessageCount == 50);
    }

    [Fact]
    public async Task GetTotalEmailCount_CountsCorrectly()
    {
        await _store.UpsertEmailAsync(CreateTestEmail(accountName: "a1", uid: 1, messageId: "m1@t.com"));
        await _store.UpsertEmailAsync(CreateTestEmail(accountName: "a1", uid: 2, messageId: "m2@t.com"));
        await _store.UpsertEmailAsync(CreateTestEmail(accountName: "a2", uid: 1, messageId: "m3@t.com"));

        Assert.Equal(3, await _store.GetTotalEmailCountAsync());
        Assert.Equal(2, await _store.GetTotalEmailCountAsync("a1"));
        Assert.Equal(1, await _store.GetTotalEmailCountAsync("a2"));
    }

    [Fact]
    public async Task DeleteByMessageId_RemovesEmail()
    {
        await _store.UpsertEmailAsync(CreateTestEmail());

        await _store.DeleteByMessageIdAsync("msg1@test.com");

        var result = await _store.GetByMessageIdAsync("msg1@test.com");
        Assert.Null(result);
    }

    public void Dispose()
    {
        _store.Dispose();
    }
}
