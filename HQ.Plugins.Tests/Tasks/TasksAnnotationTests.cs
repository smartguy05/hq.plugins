using HQ.Plugins.Tasks;

namespace HQ.Plugins.Tests.Tasks;

public class TasksAnnotationTests
{
    private static readonly string[] ExpectedToolNames =
    {
        "list_projects",
        "create_project",
        "update_project",
        "delete_project",
        "list_tasks",
        "create_task",
        "update_task",
        "complete_task",
        "delete_task",
        "add_comment",
        "list_comments",
    };

    [Fact]
    public void GetToolDefinitions_DiscoversAllTools()
    {
        // Regression guard: tool methods must use the (ServiceConfig, ServiceRequest)
        // signature so ServiceExtensions.GetServiceToolCalls<T>() can discover them.
        // A wrong first-parameter type silently yields an empty tool list.
        var tools = new TasksCommand().GetToolDefinitions();
        Assert.Equal(ExpectedToolNames.Length, tools.Count);
    }

    [Theory]
    [InlineData("list_projects")]
    [InlineData("create_project")]
    [InlineData("update_project")]
    [InlineData("delete_project")]
    [InlineData("list_tasks")]
    [InlineData("create_task")]
    [InlineData("update_task")]
    [InlineData("complete_task")]
    [InlineData("delete_task")]
    [InlineData("add_comment")]
    [InlineData("list_comments")]
    public void GetToolDefinitions_ExposesExpectedToolName(string toolName)
    {
        var tools = new TasksCommand().GetToolDefinitions();
        var names = tools.Select(t => t.Function?.Name).ToList();
        Assert.Contains(toolName, names);
    }
}
