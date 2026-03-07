using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInServiceAnnotationTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(LinkedInService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void LinkedInService_HasExpectedToolMethodCount()
    {
        var methods = GetToolMethods();
        Assert.Equal(12, methods.Count());
    }

    [Fact]
    public void LinkedInService_AllToolMethods_HaveDisplayAttribute()
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
    public void LinkedInService_AllToolMethods_HaveDescriptionAttribute()
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
    [InlineData("create_post")]
    [InlineData("get_profile")]
    [InlineData("lookup_person")]
    [InlineData("search_people")]
    [InlineData("lookup_company")]
    [InlineData("search_companies")]
    [InlineData("get_posts")]
    [InlineData("delete_post")]
    public void LinkedInService_HasExpectedToolName(string toolName)
    {
        var methods = GetToolMethods();
        var displayNames = methods.Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name).ToList();
        Assert.Contains(toolName, displayNames);
    }
}
