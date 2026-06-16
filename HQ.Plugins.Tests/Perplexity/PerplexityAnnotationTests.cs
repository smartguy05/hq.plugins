using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Perplexity;

namespace HQ.Plugins.Tests.Perplexity;

public class PerplexityAnnotationTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(PerplexityCommand).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void PerplexityCommand_HasExpectedToolMethodCount()
    {
        var methods = GetToolMethods();
        Assert.Equal(2, methods.Count());
    }

    [Fact]
    public void PerplexityCommand_AllToolMethods_HaveDisplayAttribute()
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
    public void PerplexityCommand_AllToolMethods_HaveDescriptionAttribute()
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
    public void PerplexityCommand_AllToolMethods_HaveParametersAttribute()
    {
        var methods = GetToolMethods();
        foreach (var method in methods)
        {
            var param = method.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
            Assert.NotNull(param);
            Assert.False(string.IsNullOrWhiteSpace(param.FunctionParameters),
                $"Method {method.Name} has empty Parameters");

            // Validate JSON is parseable
            var doc = JsonDocument.Parse(param.FunctionParameters);
            Assert.NotNull(doc);
        }
    }

    [Theory]
    [InlineData("perplexity_search")]
    [InlineData("perplexity_deep_research")]
    public void PerplexityCommand_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
