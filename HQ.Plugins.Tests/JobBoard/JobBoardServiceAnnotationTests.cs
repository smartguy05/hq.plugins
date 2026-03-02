using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HQ.Models.Interfaces;
using HQ.Plugins.JobBoard;

namespace HQ.Plugins.Tests.JobBoard;

public class JobBoardServiceAnnotationTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(JobBoardService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void JobBoardService_HasExpectedToolMethodCount()
    {
        var methods = GetToolMethods();
        Assert.Equal(6, methods.Count());
    }

    [Fact]
    public void JobBoardService_AllToolMethods_HaveDisplayAttribute()
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
    public void JobBoardService_AllToolMethods_HaveDescriptionAttribute()
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
    [InlineData("search_jobs")]
    [InlineData("get_job_details")]
    [InlineData("track_application")]
    [InlineData("update_application")]
    [InlineData("get_applications")]
    [InlineData("get_job_summary")]
    public void JobBoardService_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
