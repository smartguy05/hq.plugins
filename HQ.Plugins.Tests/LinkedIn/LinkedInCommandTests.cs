using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new LinkedInCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(12, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new LinkedInCommand();
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
        var command = new LinkedInCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Theory]
    [InlineData("create_post")]
    [InlineData("get_profile")]
    [InlineData("lookup_person")]
    [InlineData("search_people")]
    [InlineData("lookup_company")]
    [InlineData("search_companies")]
    [InlineData("get_posts")]
    [InlineData("delete_post")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new LinkedInCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsLinkedIn()
    {
        var command = new LinkedInCommand();
        Assert.Equal("LinkedIn", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new LinkedInCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
