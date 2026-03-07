using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams;

namespace HQ.Plugins.Tests.Teams;

public class TeamsServiceAnnotationTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(TeamsCommand).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void TeamsCommand_HasExpectedToolMethodCount()
    {
        var methods = GetToolMethods();
        Assert.Equal(5, methods.Count());
    }

    [Fact]
    public void TeamsCommand_AllToolMethods_HaveDisplayAttribute()
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
    public void TeamsCommand_AllToolMethods_HaveDescriptionAttribute()
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
    public void TeamsCommand_AllToolMethods_HaveParametersAttribute()
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
    [InlineData("send_teams_message")]
    [InlineData("list_teams")]
    [InlineData("list_teams_channels")]
    [InlineData("send_teams_file")]
    [InlineData("download_teams_file")]
    public void TeamsCommand_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
