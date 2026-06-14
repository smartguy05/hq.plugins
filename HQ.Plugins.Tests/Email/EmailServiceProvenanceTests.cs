using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Models.Safety;
using HQ.Plugins.Email;
using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;
using Moq;

namespace HQ.Plugins.Tests.Email;

/// <summary>
/// Verifies that EmailService wraps externally-sourced content in <see cref="Untrusted{T}"/>
/// envelopes when the sender is not whitelisted. Classification itself is the host's job — these
/// tests only check that the plugin emits the right provenance markers.
/// </summary>
public class EmailServiceProvenanceTests : IDisposable
{
    private readonly LocalEmailStore _store;
    private readonly EmailService _service;
    private readonly ServiceConfig _config;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new UntrustedJsonConverterFactory() }
    };

    public EmailServiceProvenanceTests()
    {
        _store = new LocalEmailStore("Data Source=:memory:");
        _service = new EmailService(Mock.Of<INotificationService>(), _store);
        _config = new ServiceConfig
        {
            Name = "test",
            Description = "test",
            EmailAccounts = new List<EmailParameters>
            {
                new() { Name = "default", Email = "me@test.com", Default = true, Imap = "imap", ImapPort = 993, Smtp = "smtp", SmtpPort = 587, Username = "me", Password = "p", UseSsl = true }
            }
        };
    }

    public void Dispose() => _store.Dispose();

    private async Task SeedEmailAsync(string messageId, string fromAddress, string body)
    {
        await _store.UpsertEmailAsync(new LocalEmail
        {
            AccountName = "default",
            Folder = "INBOX",
            Uid = (uint)Math.Abs(messageId.GetHashCode()),
            MessageId = messageId,
            Subject = "test",
            FromAddress = fromAddress,
            FromName = fromAddress,
            DateSent = DateTimeOffset.UtcNow,
            BodyText = body,
            SyncedAt = DateTime.UtcNow
        });
    }

    private static JsonElement ToJson(object o) =>
        JsonDocument.Parse(JsonSerializer.Serialize(o, JsonOpts)).RootElement.Clone();

    [Fact]
    public async Task GetEmail_UntrustedSender_WrapsBodyAsUntrusted()
    {
        await SeedEmailAsync("m1@x.com", "attacker@bad.com", "hello");

        var result = await _service.GetEmail(_config, new ServiceRequest
        {
            MessageId = "m1@x.com", Account = "default"
        });

        var body = ToJson(result).GetProperty("Result").GetProperty("Body");
        Assert.True(body.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("email-body", body.GetProperty("provenance").GetString());
        Assert.Equal("attacker@bad.com", body.GetProperty("source").GetString());
        Assert.Equal("hello", body.GetProperty("value").GetString());
    }

    [Fact]
    public async Task GetEmail_SeedTrustedSender_ReturnsRawBody()
    {
        await SeedEmailAsync("m2@x.com", "boss@co.com", "hello boss");
        _config.TrustedSenderSeed = new[] { "boss@co.com" };

        var result = await _service.GetEmail(_config, new ServiceRequest
        {
            MessageId = "m2@x.com", Account = "default"
        });

        var body = ToJson(result).GetProperty("Result").GetProperty("Body");
        Assert.Equal(JsonValueKind.String, body.ValueKind);
        Assert.Equal("hello boss", body.GetString());
    }

    [Fact]
    public async Task GetEmail_SeedTrustedDomainWildcard_ReturnsRawBody()
    {
        await SeedEmailAsync("m3@x.com", "anyone@trusted.co", "hello team");
        _config.TrustedSenderSeed = new[] { "@trusted.co" };

        var result = await _service.GetEmail(_config, new ServiceRequest
        {
            MessageId = "m3@x.com", Account = "default"
        });

        var body = ToJson(result).GetProperty("Result").GetProperty("Body");
        Assert.Equal(JsonValueKind.String, body.ValueKind);
        Assert.Equal("hello team", body.GetString());
    }

    [Fact]
    public async Task GetEmail_DbTrustedSender_ReturnsRawBody()
    {
        await SeedEmailAsync("m4@x.com", "friend@x.com", "hi");
        await _store.AddTrustedSenderAsync("friend@x.com", "added by agent");

        var result = await _service.GetEmail(_config, new ServiceRequest
        {
            MessageId = "m4@x.com", Account = "default"
        });

        var body = ToJson(result).GetProperty("Result").GetProperty("Body");
        Assert.Equal(JsonValueKind.String, body.ValueKind);
        Assert.Equal("hi", body.GetString());
    }

    [Fact]
    public async Task GetEmail_NullSender_WrapsAsUnknown()
    {
        await SeedEmailAsync("m5@x.com", null, "no sender");

        var result = await _service.GetEmail(_config, new ServiceRequest
        {
            MessageId = "m5@x.com", Account = "default"
        });

        var body = ToJson(result).GetProperty("Result").GetProperty("Body");
        Assert.True(body.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("unknown", body.GetProperty("source").GetString());
    }

    [Fact]
    public async Task AddTrustedSender_StoresAndAffectsLookup()
    {
        var req = new ServiceRequest { Sender = "alice@x.com", Reason = "vetted" };

        var result = await _service.AddTrustedSender(_config, req);

        var json = ToJson(result);
        Assert.True(json.GetProperty("Success").GetBoolean());
        Assert.True(await _store.IsTrustedSenderAsync("alice@x.com"));
    }

    [Fact]
    public async Task AddTrustedSender_DomainWildcard_Accepted()
    {
        var req = new ServiceRequest { Sender = "@trusted.co", Reason = "trusted partner" };

        var result = await _service.AddTrustedSender(_config, req);

        var json = ToJson(result);
        Assert.True(json.GetProperty("Success").GetBoolean());
        Assert.True(await _store.IsTrustedSenderAsync("anyone@trusted.co"));
    }

    [Fact]
    public async Task AddTrustedSender_MalformedAddress_Rejected()
    {
        var req = new ServiceRequest { Sender = "not an email", Reason = "r" };

        var result = await _service.AddTrustedSender(_config, req);

        Assert.False(ToJson(result).GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task AddTrustedSender_MissingReason_Rejected()
    {
        var req = new ServiceRequest { Sender = "alice@x.com", Reason = "" };

        var result = await _service.AddTrustedSender(_config, req);

        Assert.False(ToJson(result).GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task RemoveTrustedSender_OfSeededEntry_Refused()
    {
        _config.TrustedSenderSeed = new[] { "boss@co.com" };
        var req = new ServiceRequest { Sender = "boss@co.com" };

        var result = await _service.RemoveTrustedSender(_config, req);

        Assert.False(ToJson(result).GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task RemoveTrustedSender_OfAgentAddedEntry_Removes()
    {
        await _store.AddTrustedSenderAsync("alice@x.com", "r");
        var req = new ServiceRequest { Sender = "alice@x.com" };

        var result = await _service.RemoveTrustedSender(_config, req);

        Assert.True(ToJson(result).GetProperty("Success").GetBoolean());
        Assert.False(await _store.IsTrustedSenderAsync("alice@x.com"));
    }

    [Fact]
    public async Task ListTrustedSenders_ReturnsSeedAndAgentAdded()
    {
        _config.TrustedSenderSeed = new[] { "boss@co.com", "@trusted.co" };
        await _store.AddTrustedSenderAsync("friend@x.com", "vetted");

        var result = await _service.ListTrustedSenders(_config, new ServiceRequest());

        var json = ToJson(result);
        Assert.True(json.GetProperty("Success").GetBoolean());

        var seed = json.GetProperty("Seed").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("boss@co.com", seed);
        Assert.Contains("@trusted.co", seed);

        var agentAdded = json.GetProperty("AgentAdded").EnumerateArray().ToList();
        Assert.Single(agentAdded);
        Assert.Equal("friend@x.com", agentAdded[0].GetProperty("Email").GetString());
    }
}
