using HQ.Plugins.Asana;

namespace HQ.Plugins.Tests.Asana;

public class AsanaCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new AsanaCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(16, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new AsanaCommand();
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
        var command = new AsanaCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsAreFunction()
    {
        var command = new AsanaCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.Equal("function", t.Type);
        });
    }

    [Theory]
    [InlineData("list_workspaces")]
    [InlineData("get_user")]
    [InlineData("create_task")]
    [InlineData("get_task")]
    [InlineData("update_task")]
    [InlineData("delete_task")]
    [InlineData("search_tasks")]
    [InlineData("get_tasks")]
    [InlineData("set_parent_for_task")]
    [InlineData("add_task_followers")]
    [InlineData("get_projects")]
    [InlineData("get_project")]
    [InlineData("get_project_sections")]
    [InlineData("create_task_story")]
    [InlineData("get_stories_for_task")]
    [InlineData("typeahead_search")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new AsanaCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsAsana()
    {
        var command = new AsanaCommand();
        Assert.Equal("Asana", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new AsanaCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
