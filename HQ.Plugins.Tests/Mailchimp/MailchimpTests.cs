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
            var schema = HQ.Models.Helpers.ToolSchemaGenerator.Generate(p!.ArgsType);
            Assert.False(string.IsNullOrWhiteSpace(schema), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(schema));
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
