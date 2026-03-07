using HQ.Plugins.Teams;

namespace HQ.Plugins.Tests.Teams;

public class TeamsCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new TeamsCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(5, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new TeamsCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function);
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Description));
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveParameters()
    {
        var command = new TeamsCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsAreFunction()
    {
        var command = new TeamsCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.Equal("function", t.Type);
        });
    }

    [Theory]
    [InlineData("send_teams_message")]
    [InlineData("list_teams")]
    [InlineData("list_teams_channels")]
    [InlineData("send_teams_file")]
    [InlineData("download_teams_file")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new TeamsCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsTeams()
    {
        var command = new TeamsCommand();
        Assert.Equal("Teams", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new TeamsCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
