using HQ.Plugins.HubSpot;

namespace HQ.Plugins.Tests.HubSpot;

public class HubSpotCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new HubSpotCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(10, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new HubSpotCommand();
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
        var command = new HubSpotCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsAreFunction()
    {
        var command = new HubSpotCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.Equal("function", t.Type);
        });
    }

    [Theory]
    [InlineData("create_contact")]
    [InlineData("update_contact")]
    [InlineData("search_contacts")]
    [InlineData("get_contact")]
    [InlineData("create_deal")]
    [InlineData("update_deal")]
    [InlineData("search_deals")]
    [InlineData("create_company")]
    [InlineData("search_companies")]
    [InlineData("add_note")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new HubSpotCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsHubSpot()
    {
        var command = new HubSpotCommand();
        Assert.Equal("HubSpot", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new HubSpotCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
