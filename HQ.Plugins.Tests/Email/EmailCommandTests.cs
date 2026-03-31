using System.Reflection;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Email;

namespace HQ.Plugins.Tests.Email;

public class EmailCommandTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    private string CreateTempPluginDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"email-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        // EmailService has 17 annotated tool methods
        var command = new EmailCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(17, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new EmailCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function);
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Description));
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveTypeFunction()
    {
        var command = new EmailCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t => Assert.Equal("function", t.Type));
    }

    [Fact]
    public async Task Initialize_DifferentAgents_GetIndependentStores()
    {
        // Arrange
        var agentIdA = Guid.NewGuid().ToString();
        var agentIdB = Guid.NewGuid().ToString();
        var tempDir = CreateTempPluginDir();

        var configA = $@"{{""agentId"":""{agentIdA}"",""sqliteConnectionString"":""Data Source={Path.Combine(tempDir, "a.db").Replace("\\", "\\\\")}""}}";
        var configB = $@"{{""agentId"":""{agentIdB}"",""sqliteConnectionString"":""Data Source={Path.Combine(tempDir, "b.db").Replace("\\", "\\\\")}""}}";

        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;

        var commandA = new EmailCommand();
        var commandB = new EmailCommand();

        // Act
        await commandA.Initialize(configA, logger, null);
        await commandB.Initialize(configB, logger, null);

        // Assert - each instance should have its own store (not shared via static)
        var storeField = typeof(EmailCommand).GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(storeField); // Fails if still static (would need BindingFlags.Static)

        var storeA = storeField.GetValue(commandA);
        var storeB = storeField.GetValue(commandB);

        Assert.NotNull(storeA);
        Assert.NotNull(storeB);
        Assert.NotSame(storeA, storeB);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
