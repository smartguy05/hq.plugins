using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Notion;

namespace HQ.Plugins.Tests.Notion;

public class NotionTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(NotionService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(6, methods.Count);
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
    [InlineData("notion_search")]
    [InlineData("notion_get_page")]
    [InlineData("notion_create_page")]
    [InlineData("notion_append_block")]
    [InlineData("notion_query_database")]
    [InlineData("notion_update_page")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Theory]
    [InlineData("one line", 1)]
    [InlineData("a\n\nb", 2)]
    [InlineData("a\nb\nc", 3)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParagraphBlocks_OnePerNonEmptyLine(string text, int expected) =>
        Assert.Equal(expected, NotionService.ParagraphBlocks(text).Count);

    [Fact]
    public void RichText_WrapsContent()
    {
        var rt = NotionService.RichText("hello");
        Assert.Single(rt);
        Assert.Equal("hello", rt[0]!["text"]!["content"]!.GetValue<string>());
    }
}
