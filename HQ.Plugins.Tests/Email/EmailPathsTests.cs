using HQ.Plugins.Email;

namespace HQ.Plugins.Tests.Email;

public class EmailPathsTests
{
    [Fact]
    public void DbFileName_WithAgentId_IsAgentScoped()
    {
        Assert.Equal("agent-abc123-emails.db", EmailPaths.DbFileName("abc123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DbFileName_WithoutAgentId_IsShared(string agentId)
    {
        Assert.Equal("emails.db", EmailPaths.DbFileName(agentId));
    }

    [Fact]
    public void ResolveConnectionString_PointsAtTheAgentsDbFile()
    {
        var conn = EmailPaths.ResolveConnectionString("abc123");
        Assert.StartsWith("Data Source=", conn);
        Assert.Contains("agent-abc123-emails.db", conn);
        Assert.Contains("EmailData", conn);
    }

    [Fact]
    public void EmailDataDir_HonorsEnvOverride_WhenSet()
    {
        const string varName = "HQ_EMAIL_DATA_DIR";
        var original = Environment.GetEnvironmentVariable(varName);
        try
        {
            Environment.SetEnvironmentVariable(varName, "/var/lib/hq/email-data");
            Assert.Equal("/var/lib/hq/email-data", EmailPaths.EmailDataDir());
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, original);
        }
    }

    [Fact]
    public void EmailDataDir_FallsBackToPluginDir_WhenEnvUnset()
    {
        const string varName = "HQ_EMAIL_DATA_DIR";
        var original = Environment.GetEnvironmentVariable(varName);
        try
        {
            Environment.SetEnvironmentVariable(varName, null);
            Assert.EndsWith("EmailData", EmailPaths.EmailDataDir());
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, original);
        }
    }

    [Fact]
    public void AgentIdFromFileName_RoundTrips()
    {
        var id = "11112222-3333-4444-5555-666677778888";
        Assert.Equal(id, EmailPaths.AgentIdFromFileName(EmailPaths.DbFileName(id)));
    }

    [Theory]
    [InlineData("emails.db")]
    [InlineData("agent-emails.db")]      // missing id segment shape
    [InlineData("notes.txt")]
    [InlineData("")]
    public void AgentIdFromFileName_ReturnsNullForNonAgentFiles(string fileName)
    {
        // "agent-emails.db" decodes to an empty id; treat empty as no-agent.
        var result = EmailPaths.AgentIdFromFileName(fileName);
        Assert.True(string.IsNullOrEmpty(result));
    }
}
