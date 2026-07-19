using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Ramp;

namespace HQ.Plugins.Tests.Ramp;

public class RampTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(RampService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>() != null);

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(8, methods.Count);
        foreach (var m in methods)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DisplayAttribute>()?.Name), $"{m.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), $"{m.Name} missing Description");
            var p = m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
            Assert.NotNull(p?.ArgsType);
            var schema = HQ.Models.Helpers.ToolSchemaGenerator.Generate(p!.ArgsType);
            Assert.False(string.IsNullOrWhiteSpace(schema), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(schema));
        }
    }

    [Theory]
    [InlineData("list_transactions")]
    [InlineData("list_cards")]
    [InlineData("list_reimbursements")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));
}
