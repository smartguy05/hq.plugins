using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Zendesk;

namespace HQ.Plugins.Tests.Zendesk;

public class ZendeskTests
{
    // Tool methods are now (ServiceConfig config, TArgs args) with a [Parameters(typeof(TArgs))]
    // attribute driving the generated schema — mirrors the migrated-plugin convention.
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(ZendeskService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>() != null);

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(10, methods.Count);
        foreach (var m in methods)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DisplayAttribute>()?.Name), $"{m.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), $"{m.Name} missing Description");
            var p = m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
            Assert.NotNull(p?.ArgsType);
            // The args type must yield a valid OpenAI parameter schema.
            Assert.NotNull(JsonDocument.Parse(HQ.Models.Helpers.ToolSchemaGenerator.Generate(p!.ArgsType)));
        }
    }

    [Theory]
    [InlineData("search_tickets")]
    [InlineData("create_ticket")]
    [InlineData("add_ticket_comment")]
    [InlineData("apply_macro")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));
}
