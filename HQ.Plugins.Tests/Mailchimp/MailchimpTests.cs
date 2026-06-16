using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Mailchimp;

namespace HQ.Plugins.Tests.Mailchimp;

public class MailchimpTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(MailchimpService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));

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
            Assert.False(string.IsNullOrWhiteSpace(p?.FunctionParameters), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(p!.FunctionParameters));
        }
    }

    [Fact]
    public void SendCampaign_SupportsConfirmation()
    {
        var m = ToolMethods().First(x => x.GetCustomAttribute<DisplayAttribute>()?.Name == "send_campaign");
        Assert.NotNull(m.GetCustomAttribute<HQ.Models.Helpers.SupportsConfirmationAttribute>());
    }

    [Theory]
    [InlineData("abc123-us21", "us21")]
    [InlineData("key-with-many-dashes-us6", "us6")]
    public void DataCenterFromKey_ParsesSuffix(string key, string expected) =>
        Assert.Equal(expected, MailchimpClient.DataCenterFromKey(key));

    [Theory]
    [InlineData("no-datacenter-suffix-")]
    [InlineData("nodash")]
    public void DataCenterFromKey_RejectsBadKeys(string key) =>
        Assert.Throws<InvalidOperationException>(() => MailchimpClient.DataCenterFromKey(key));

    [Fact]
    public void SubscriberHash_IsLowercaseMd5OfLowercasedEmail() =>
        // MD5("jane@acme.com") — case-insensitive input must normalize to the same hash
        Assert.Equal(MailchimpClient.SubscriberHash("jane@acme.com"), MailchimpClient.SubscriberHash("JANE@Acme.com"));
}
