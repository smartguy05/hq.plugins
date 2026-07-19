using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Health;

namespace HQ.Plugins.Tests.Health;

public class HealthTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(HealthService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>() != null);

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
            Assert.NotNull(p?.ArgsType);
            var schema = HQ.Models.Helpers.ToolSchemaGenerator.Generate(p!.ArgsType);
            Assert.False(string.IsNullOrWhiteSpace(schema), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(schema));
        }
    }

    [Theory]
    [InlineData("list_health_users")]
    [InlineData("get_sleep")]
    [InlineData("get_activity")]
    [InlineData("get_daily")]
    [InlineData("get_body")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Fact]
    public void DataPath_BuildsQuery() =>
        Assert.Equal("/sleep?user_id=abc&start_date=2026-01-01&end_date=2026-01-08",
            HealthService.DataPath("sleep", "abc", "2026-01-01", "2026-01-08"));

    [Fact]
    public void Date_UsesFallbackWhenEmpty()
    {
        var fallback = new DateTime(2026, 3, 1);
        Assert.Equal("2026-03-01", HealthService.Date(" ", fallback));
        Assert.Equal("2026-03-10", HealthService.Date("2026-03-10", fallback));
    }
}
