using System.Collections.Concurrent;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Email.Data;
using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Tests.Email;

public class EmailSyncEngineTests : IDisposable
{
    private readonly LocalEmailStore _store = new("Data Source=:memory:");
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _logs = new();

    private EmailSyncEngine CreateEngine(ServiceConfig config)
    {
        LogDelegate logger = (level, msg, _) =>
        {
            _logs.Enqueue((level, msg));
            return Task.CompletedTask;
        };
        // vectorService null = ChromaDB not configured (valid per constructor)
        return new EmailSyncEngine(_store, null, config, logger);
    }

    [Fact]
    public async Task SyncAllAccounts_SkipsAccountWithBlankImapHost_DoesNotThrow()
    {
        var config = new ServiceConfig
        {
            EmailAccounts = new[]
            {
                new EmailParameters { Name = "", Imap = "" } // half-configured: blank host AND blank name
            }
        };
        var engine = CreateEngine(config);

        // Must not throw "The host name cannot be empty".
        var result = await engine.SyncAllAccountsAsync();

        // Result shape stays consistent and the account is reported as skipped.
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"Success\":true", json);
        Assert.Contains("Skipped", json);
        Assert.Contains("No IMAP host configured.", json);

        // Skip is logged at Info (not Error) and never produces a bare "for : ".
        Assert.Contains(_logs, l => l.Level == LogLevel.Info && l.Message.Contains("<unnamed account>"));
        Assert.DoesNotContain(_logs, l => l.Level == LogLevel.Error);
    }

    [Fact]
    public async Task SyncAllAccounts_NoAccounts_Succeeds()
    {
        var engine = CreateEngine(new ServiceConfig { EmailAccounts = null });

        var result = await engine.SyncAllAccountsAsync();

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("\"Success\":true", json);
        Assert.DoesNotContain(_logs, l => l.Level == LogLevel.Error);
    }

    public void Dispose() => _store.Dispose();
}
