using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleContacts;

namespace HQ.Plugins.Tests.GoogleContacts;

public class GoogleContactsTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(GoogleContactsService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType) &&
                        m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        !m.GetParameters()[1].ParameterType.IsValueType &&
                        !typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[1].ParameterType));

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
    [InlineData("list_contacts")]
    [InlineData("search_contacts")]
    [InlineData("get_contact")]
    [InlineData("create_contact")]
    [InlineData("update_contact")]
    public void ExposesExpectedTool(string toolName) =>
        Assert.Contains(toolName, ToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name));

    [Fact]
    public void BuildPerson_SetsProvidedFields()
    {
        var p = GoogleContactsService.BuildPerson("Ada", "Lovelace", "ada@example.com", "555-0100", "Analytical Engines");
        Assert.Equal("Ada", p.Names[0].GivenName);
        Assert.Equal("Lovelace", p.Names[0].FamilyName);
        Assert.Equal("ada@example.com", p.EmailAddresses[0].Value);
        Assert.Equal("555-0100", p.PhoneNumbers[0].Value);
        Assert.Equal("Analytical Engines", p.Organizations[0].Name);
    }

    [Fact]
    public void BuildPerson_OmitsUnsetFields()
    {
        var p = GoogleContactsService.BuildPerson("OnlyName", null, null, null, null);
        Assert.NotNull(p.Names);
        Assert.Null(p.EmailAddresses);
        Assert.Null(p.PhoneNumbers);
        Assert.Null(p.Organizations);
    }
}
