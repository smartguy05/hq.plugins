using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Plaid;

namespace HQ.Plugins.Tests.Plaid;

public class PlaidTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(PlaidService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>() != null);

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
            Assert.False(string.IsNullOrWhiteSpace(schema), $"{m.Name} missing Parameters schema");
            Assert.NotNull(JsonDocument.Parse(schema));
        }
    }

    [Theory]
    [InlineData("list_accounts")]
    [InlineData("get_balances")]
    [InlineData("list_transactions")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Theory]
    [InlineData("production", "https://production.plaid.com")]
    [InlineData("PROD", "https://production.plaid.com")]
    [InlineData("sandbox", "https://sandbox.plaid.com")]
    [InlineData("", "https://sandbox.plaid.com")]
    [InlineData(null, "https://sandbox.plaid.com")]
    public void BaseUrlFor_SelectsEnvironment(string env, string expected) =>
        Assert.Equal(expected, PlaidService.BaseUrlFor(env));

    [Fact]
    public void Date_UsesFallbackWhenEmpty()
    {
        var fallback = new DateTime(2026, 1, 15);
        Assert.Equal("2026-01-15", PlaidService.Date(null, fallback));
        Assert.Equal("2026-02-20", PlaidService.Date("2026-02-20", fallback));
    }
}
