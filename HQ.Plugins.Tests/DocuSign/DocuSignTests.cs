using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.DocuSign;

namespace HQ.Plugins.Tests.DocuSign;

public class DocuSignTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(DocuSignService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(7, methods.Count);
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
    [InlineData("send_envelope")]
    [InlineData("get_envelope_status")]
    [InlineData("download_completed_document")]
    [InlineData("void_envelope")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Theory]
    [InlineData("send_envelope")]
    [InlineData("send_envelope_from_template")]
    [InlineData("void_envelope")]
    public void OutwardFacingTools_SupportConfirmation(string toolName)
    {
        var method = ToolMethods().First(m => m.GetCustomAttribute<DisplayAttribute>()?.Name == toolName);
        Assert.NotNull(method.GetCustomAttribute<HQ.Models.Helpers.SupportsConfirmationAttribute>());
    }
}
