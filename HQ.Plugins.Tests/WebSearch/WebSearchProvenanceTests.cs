using System.Net;
using System.Text.Json;
using HQ.Models.Safety;
using HQ.Plugins.WebSearch;
using HQ.Plugins.WebSearch.Models;

namespace HQ.Plugins.Tests.WebSearch;

/// <summary>
/// Verifies that WebSearch wraps the raw search-provider JSON body (open-web content, highest
/// prompt-injection risk) in a single <see cref="Untrusted{T}"/> envelope sourced to the search
/// API host. The LLM receives the JSON as a string inside the envelope, only after the host has
/// issued a safe verdict.
/// </summary>
public class WebSearchProvenanceTests
{
    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Converters = { new UntrustedJsonConverterFactory() }
    };

    private static JsonElement ToJson(object o) =>
        JsonDocument.Parse(JsonSerializer.Serialize(o, JsonOpts)).RootElement.Clone();

    [Fact]
    public async Task WebSearch_WrapsRawResultsAsUntrusted()
    {
        const string raw = """{"web":{"results":[{"title":"t","url":"https://example.com"}]}}""";

        var cmd = new WebSearchCommand();
        cmd.SetHttpMessageHandler(new StubHandler(raw));

        var config = new ServiceConfig
        {
            Name = "Web Search",
            WebSearchUrl = "https://api.search.brave.com/res/v1/web/search"
        };

        var result = await cmd.WebSearch(config, new WebSearchArgs { Query = "test" });

        var json = ToJson(result);
        Assert.True(json.GetProperty("Success").GetBoolean());

        var results = json.GetProperty("Results");
        Assert.True(results.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("web-search-results", results.GetProperty("provenance").GetString());
        Assert.Equal("api.search.brave.com", results.GetProperty("source").GetString());
        Assert.Equal(raw, results.GetProperty("value").GetString());
    }
}
