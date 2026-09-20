using System.Net;
using System.Text;
using System.Text.Json;
using HQ.Plugins.SupportChannelKb;
using HQ.Plugins.SupportChannelKb.Models;
using Moq;
using Moq.Protected;

namespace HQ.Plugins.Tests.SupportChannelKb;

/// <summary>
/// WP6B-12: the upstream KB service's /collections response carries a per-collection api_key.
/// The Collection DTO modeled that field and GetCollections() returned the array verbatim as the
/// get_support_channel_collections tool result — serializing the upstream API key straight into
/// the LLM conversation and the persisted trace/debug log. Nothing in this plugin ever reads
/// Collection.ApiKey (auth uses _config.DefaultChannelApiKey), so the field exists only to leak.
/// </summary>
public class CollectionRedactionTests
{
    private static ServiceConfig Config() => new()
    {
        Name = "test",
        Description = "test",
        SupportChannelKbUrl = "http://kb.local",
        DefaultSaveChannel = "support",
        DefaultChannelApiKey = "internal-key"
    };

    private static SupportChannelKbService Service(string responseJson)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        return new SupportChannelKbService(Config()) { HttpHandler = handler.Object };
    }

    [Fact]
    public async Task GetCollections_DoesNotLeakUpstreamApiKey()
    {
        const string upstreamResponse = """
            [{"name":"support","description":"Support KB","created":"2026-01-01","api_key":"sk-upstream-secret-123"}]
            """;
        var service = Service(upstreamResponse);

        var result = await service.GetCollections();

        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("sk-upstream-secret-123", json);
    }

    [Fact]
    public void Collection_HasNoApiKeyProperty()
    {
        // A property named ApiKey (or bound to the "api_key" JSON field) would be serialized
        // straight into the tool result and the persisted trace/debug log — ratchet against it
        // coming back.
        var props = typeof(Collection).GetProperties();
        Assert.DoesNotContain(props, p =>
            p.Name.Contains("ApiKey", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
            p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }
}
