using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Memories;

namespace HQ.Plugins.Tests.Memories;

public class ChromaServiceAnnotationTests
{
    private static readonly string[] ExpectedToolNames = new[]
    {
        "memory_health_check",
        "add_memory",
        "find_memory",
        "get_memory",
        "delete_memory",
        "edit_memory"
    };

    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(ChromaService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void ChromaService_GetServiceToolCalls_ReturnsExpectedToolCount()
    {
        // ChromaService has 6 tool methods
        var methods = GetToolMethods();
        Assert.Equal(6, methods.Count());
    }

    [Fact]
    public void ChromaService_AllToolMethods_HaveDisplayAttribute()
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
    public void ChromaService_AllToolMethods_HaveDescriptionAttribute()
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

    [Fact]
    public void ChromaService_AllToolMethods_HaveParametersAttribute()
    {
        var methods = GetToolMethods();
        foreach (var method in methods)
        {
            var param = method.GetCustomAttribute<ParametersAttribute>();
            Assert.NotNull(param);
            Assert.False(string.IsNullOrWhiteSpace(param.FunctionParameters),
                $"Method {method.Name} has empty Parameters");
        }
    }

    [Theory]
    [InlineData("memory_health_check")]
    [InlineData("add_memory")]
    [InlineData("find_memory")]
    [InlineData("get_memory")]
    [InlineData("delete_memory")]
    [InlineData("edit_memory")]
    public void ChromaService_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetDisplay()).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
