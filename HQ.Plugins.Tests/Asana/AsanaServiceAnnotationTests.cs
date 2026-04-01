using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HQ.Models.Interfaces;
using HQ.Plugins.Asana;

namespace HQ.Plugins.Tests.Asana;

public class AsanaServiceAnnotationTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(AsanaService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void AsanaService_HasExpectedToolMethodCount()
    {
        var methods = GetToolMethods();
        Assert.Equal(16, methods.Count());
    }

    [Fact]
    public void AsanaService_AllToolMethods_HaveDisplayAttribute()
    {
        var methods = GetToolMethods();
        foreach (var method in methods)
        {
            var display = method.GetCustomAttribute<DisplayAttribute>();
            Assert.NotNull(display);
            Assert.False(string.IsNullOrWhiteSpace(display.Name),
                $"Method {method.Name} has empty Display.Name");
        }
    }

    [Fact]
    public void AsanaService_AllToolMethods_HaveDescriptionAttribute()
    {
        var methods = GetToolMethods();
        foreach (var method in methods)
        {
            var desc = method.GetCustomAttribute<DescriptionAttribute>();
            Assert.NotNull(desc);
            Assert.False(string.IsNullOrWhiteSpace(desc.Description),
                $"Method {method.Name} has empty Description");
        }
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
    public void AsanaService_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
