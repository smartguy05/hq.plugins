using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HQ.Models.Interfaces;
using HQ.Plugins.HubSpot;

namespace HQ.Plugins.Tests.HubSpot;

public class HubSpotServiceAnnotationTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(HubSpotService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void HubSpotService_HasExpectedToolMethodCount()
    {
        var methods = GetToolMethods();
        Assert.Equal(10, methods.Count());
    }

    [Fact]
    public void HubSpotService_AllToolMethods_HaveDisplayAttribute()
    {
        var methods = GetToolMethods();
        foreach (var method in methods)
        {
            var display = method.GetCustomAttribute<DisplayAttribute>();
            Assert.NotNull(display);
            Assert.False(string.IsNullOrWhiteSpace(display.Name),
                $"Method {method.Name} has empty Display.Name");
        }
    }

    [Fact]
    public void HubSpotService_AllToolMethods_HaveDescriptionAttribute()
    {
        var methods = GetToolMethods();
        foreach (var method in methods)
        {
            var desc = method.GetCustomAttribute<DescriptionAttribute>();
            Assert.NotNull(desc);
            Assert.False(string.IsNullOrWhiteSpace(desc.Description),
                $"Method {method.Name} has empty Description");
        }
    }

    [Theory]
    [InlineData("create_contact")]
    [InlineData("update_contact")]
    [InlineData("search_contacts")]
    [InlineData("get_contact")]
    [InlineData("create_deal")]
    [InlineData("update_deal")]
    [InlineData("search_deals")]
    [InlineData("create_company")]
    [InlineData("search_companies")]
    [InlineData("add_note")]
    public void HubSpotService_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
