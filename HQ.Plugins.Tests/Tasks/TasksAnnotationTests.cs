using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Helpers;
using HQ.Plugins.Tasks;
using HQ.Plugins.Tasks.Tools;

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

    [Fact]
    public void CreateTask_DoesNotRequireProjectId()
    {
        // A project-less task is private to the calling agent, so projectId must be optional.
        var method = typeof(TasksToolImpl).GetMethods()
            .Single(m => m.GetCustomAttribute<DisplayAttribute>()?.Name == "create_task");
        var json = method.GetCustomAttribute<ParametersAttribute>()!.FunctionParameters;

        using var doc = JsonDocument.Parse(json);
        var required = doc.RootElement.GetProperty("required")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        Assert.DoesNotContain("projectId", required);
        Assert.Contains("title", required);
    }
}
