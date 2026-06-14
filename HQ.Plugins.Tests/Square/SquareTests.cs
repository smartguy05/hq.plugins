using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Square;

namespace HQ.Plugins.Tests.Square;

public class SquareTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(SquareService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(12, methods.Count);
        foreach (var m in methods)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DisplayAttribute>()?.Name), $"{m.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), $"{m.Name} missing Description");
            var p = m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
            Assert.False(string.IsNullOrWhiteSpace(p?.FunctionParameters), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(p!.FunctionParameters));
        }
    }

    [Theory]
    [InlineData("list_locations")]
    [InlineData("list_catalog_items")]
    [InlineData("search_availability")]
    [InlineData("create_booking")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Theory]
    [InlineData("create_booking")]
    [InlineData("cancel_booking")]
    public void BookingWrites_SupportConfirmation(string toolName)
    {
        var m = ToolMethods().First(x => x.GetCustomAttribute<DisplayAttribute>()?.Name == toolName);
        Assert.NotNull(m.GetCustomAttribute<HQ.Models.Helpers.SupportsConfirmationAttribute>());
    }
}
