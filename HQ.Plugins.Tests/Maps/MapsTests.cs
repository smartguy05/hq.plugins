using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Maps;

namespace HQ.Plugins.Tests.Maps;

public class MapsTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(MapsService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(5, methods.Count);
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
    [InlineData("get_directions")]
    [InlineData("get_travel_time")]
    [InlineData("search_places")]
    [InlineData("get_place_details")]
    [InlineData("geocode_address")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Theory]
    [InlineData("walking", "walking")]
    [InlineData("BIKE", "bicycling")]
    [InlineData("transit", "transit")]
    [InlineData("driving", "driving")]
    [InlineData("", "driving")]
    [InlineData(null, "driving")]
    public void NormalizeMode_MapsToMapsApiValues(string input, string expected) =>
        Assert.Equal(expected, MapsService.NormalizeMode(input));
}
