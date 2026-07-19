using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.DocumentAI;

namespace HQ.Plugins.Tests.DocumentAI;

public class DocumentAiTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(DocumentAiService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType) &&
                        m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        !m.GetParameters()[1].ParameterType.IsValueType &&
                        !typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[1].ParameterType));

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(3, methods.Count);
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
    [InlineData("extract_text")]
    [InlineData("extract_receipt")]
    [InlineData("extract_document_fields")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Fact]
    public void ProcessorUrl_BuildsEndpoint() =>
        Assert.Equal(
            "https://us-documentai.googleapis.com/v1/projects/my-proj/locations/us/processors/abc123:process",
            DocumentAiService.ProcessorUrl("us", "my-proj", "abc123"));

    [Fact]
    public void ProcessorUrl_DefaultsLocationToUs() =>
        Assert.StartsWith("https://us-documentai.googleapis.com/", DocumentAiService.ProcessorUrl(" ", "p", "x"));

    [Fact]
    public void ProcessorUrl_HonorsRegion() =>
        Assert.StartsWith("https://eu-documentai.googleapis.com/", DocumentAiService.ProcessorUrl("eu", "p", "x"));
}
