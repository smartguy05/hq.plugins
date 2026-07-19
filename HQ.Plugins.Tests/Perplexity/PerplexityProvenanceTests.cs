using System.Net;
using System.Text.Json;
using HQ.Models.Safety;
using HQ.Plugins.Perplexity;
using HQ.Plugins.Perplexity.Models;

namespace HQ.Plugins.Tests.Perplexity;

/// <summary>
/// Verifies that PerplexitySearch wraps the model's answer and citations (open-web-sourced
/// content, highest prompt-injection risk) in <see cref="Untrusted{T}"/> envelopes sourced to
/// perplexity.ai. Classification itself is the host's job — these tests only check that the
/// plugin emits the right provenance markers.
/// </summary>
public class PerplexityProvenanceTests
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
    public async Task PerplexitySearch_WrapsAnswerAndCitationsAsUntrusted()
    {
        const string completion = """
            {"choices":[{"message":{"content":"Paris is the capital of France."}}],
             "citations":["https://a.example.com","https://b.example.com"]}
            """;

        var cmd = new PerplexityCommand();
        cmd.SetHttpMessageHandler(new StubHandler(completion));

        var config = new ServiceConfig { Name = "Perplexity", PerplexityApiKey = "pplx-test" };

        var result = await cmd.PerplexitySearch(config, new PerplexitySearchArgs { Query = "capital of France" });

        var json = ToJson(result);
        Assert.True(json.GetProperty("Success").GetBoolean());

        var answer = json.GetProperty("Answer");
        Assert.True(answer.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("perplexity-answer", answer.GetProperty("provenance").GetString());
        Assert.Equal("perplexity.ai", answer.GetProperty("source").GetString());
        Assert.Equal("Paris is the capital of France.", answer.GetProperty("value").GetString());

        var citations = json.GetProperty("Citations");
        Assert.True(citations.GetProperty("__untrusted").GetBoolean());
        Assert.Equal("perplexity-citations", citations.GetProperty("provenance").GetString());
        Assert.Equal("perplexity.ai", citations.GetProperty("source").GetString());
        Assert.Contains("https://a.example.com", citations.GetProperty("value").GetString());
    }
}
