using HQ.Plugins.Email.Data;

namespace HQ.Plugins.Tests.Email;

public class TrustedSenderStoreTests : IDisposable
{
    private readonly LocalEmailStore _store;

    public TrustedSenderStoreTests()
    {
        _store = new LocalEmailStore("Data Source=:memory:");
    }

    public void Dispose() => _store.Dispose();

    [Fact]
    public async Task AddAndList_RoundTrips()
    {
        await _store.AddTrustedSenderAsync("alice@example.com", "co-founder");

        var list = await _store.ListTrustedSendersAsync();

        Assert.Single(list);
        Assert.Equal("alice@example.com", list[0].Email);
        Assert.Equal("co-founder", list[0].Reason);
    }

    [Fact]
    public async Task IsTrustedSender_ExactMatch_CaseInsensitive()
    {
        await _store.AddTrustedSenderAsync("alice@example.com", "reason");

        Assert.True(await _store.IsTrustedSenderAsync("alice@example.com"));
        Assert.True(await _store.IsTrustedSenderAsync("ALICE@EXAMPLE.COM"));
        Assert.True(await _store.IsTrustedSenderAsync("Alice@Example.com"));
    }

    [Fact]
    public async Task IsTrustedSender_DomainWildcard_MatchesAnyAddress()
    {
        await _store.AddTrustedSenderAsync("@example.com", "trusted domain");

        Assert.True(await _store.IsTrustedSenderAsync("anyone@example.com"));
        Assert.True(await _store.IsTrustedSenderAsync("bob@EXAMPLE.com"));
        Assert.False(await _store.IsTrustedSenderAsync("alice@other.com"));
    }

    [Fact]
    public async Task IsTrustedSender_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(await _store.IsTrustedSenderAsync(null));
        Assert.False(await _store.IsTrustedSenderAsync(""));
        Assert.False(await _store.IsTrustedSenderAsync("   "));
        Assert.False(await _store.IsTrustedSenderAsync("not-an-email"));
    }

    [Fact]
    public async Task AddSameSender_Twice_UpdatesReasonAndTimestamp()
    {
        await _store.AddTrustedSenderAsync("bob@x.com", "first");
        var list1 = await _store.ListTrustedSendersAsync();
        var firstAddedAt = list1[0].AddedAt;
        await Task.Delay(10);

        await _store.AddTrustedSenderAsync("bob@x.com", "second");
        var list2 = await _store.ListTrustedSendersAsync();

        Assert.Single(list2);
        Assert.Equal("second", list2[0].Reason);
        Assert.True(list2[0].AddedAt > firstAddedAt);
    }

    [Fact]
    public async Task RemoveTrustedSender_ReturnsTrueWhenExisted()
    {
        await _store.AddTrustedSenderAsync("alice@x.com", "r");

        Assert.True(await _store.RemoveTrustedSenderAsync("alice@x.com"));
        Assert.False(await _store.IsTrustedSenderAsync("alice@x.com"));
    }

    [Fact]
    public async Task RemoveTrustedSender_ReturnsFalseWhenAbsent()
    {
        Assert.False(await _store.RemoveTrustedSenderAsync("nobody@x.com"));
    }

    [Fact]
    public async Task AddTrustedSender_EmptyEntry_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.AddTrustedSenderAsync("", "reason"));
    }
}
