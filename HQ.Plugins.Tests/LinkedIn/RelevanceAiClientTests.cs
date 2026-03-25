using System.Net;
using System.Text.Json;
using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

public class RelevanceAiClientTests : IDisposable
{
    private const string TestApiKey = "test-api-key-123";
    private const string TestRegion = "us-east";
    private const string TestProjectId = "proj-abc";
    private readonly RelevanceAiClient _client;
    private readonly MockHttpMessageHandler _handler;

    public RelevanceAiClientTests()
    {
        _handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_handler);
        _client = new RelevanceAiClient(TestApiKey, TestRegion, TestProjectId, httpClient);
    }

    public void Dispose()
    {
        _client.Dispose();
    }

    [Fact]
    public async Task TriggerTool_SendsPostToCorrectUrl()
    {
        _handler.ResponseContent = """{"output": "ok"}""";

        await _client.TriggerTool("tool-123", new Dictionary<string, object> { { "key", "value" } });

        Assert.Equal(HttpMethod.Post, _handler.LastRequest.Method);
        Assert.Equal(
            $"https://api-{TestRegion}.stack.tryrelevance.com/latest/studios/tool-123/trigger_limited",
            _handler.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task TriggerTool_SendsCorrectAuthHeader()
    {
        _handler.ResponseContent = """{"output": "ok"}""";

        await _client.TriggerTool("tool-123", new Dictionary<string, object>());

        Assert.Equal(TestApiKey, _handler.LastRequest.Headers.GetValues("Authorization").First());
    }

    [Fact]
    public async Task TriggerTool_RequestBodyContainsParamsAndProject()
    {
        _handler.ResponseContent = """{"output": "ok"}""";
        var parameters = new Dictionary<string, object> { { "username", "johndoe" } };

        await _client.TriggerTool("tool-123", parameters);

        var body = JsonSerializer.Deserialize<JsonElement>(_handler.LastRequestBody);
        Assert.True(body.TryGetProperty("params", out var paramsElement));
        Assert.Equal("johndoe", paramsElement.GetProperty("username").GetString());
        Assert.True(body.TryGetProperty("project", out var projectElement));
        Assert.Equal(TestProjectId, projectElement.GetString());
    }

    [Fact]
    public async Task TriggerTool_ThrowsOnErrorResponse()
    {
        _handler.ResponseStatusCode = HttpStatusCode.InternalServerError;
        _handler.ResponseContent = """{"error": "something went wrong"}""";

        await Assert.ThrowsAsync<HttpRequestException>(
            () => _client.TriggerTool("tool-123", new Dictionary<string, object>()));
    }

    [Fact]
    public async Task ListTools_SendsGetToCorrectUrl()
    {
        _handler.ResponseContent = """[{"name": "tool1"}]""";

        await _client.ListTools();

        Assert.Equal(HttpMethod.Get, _handler.LastRequest.Method);
        Assert.Equal(
            $"https://api-{TestRegion}.stack.tryrelevance.com/latest/studios/list?project_id={TestProjectId}",
            _handler.LastRequest.RequestUri.ToString());
    }

    [Fact]
    public async Task ListTools_SendsCorrectAuthHeader()
    {
        _handler.ResponseContent = """[{"name": "tool1"}]""";

        await _client.ListTools();

        Assert.Equal(TestApiKey, _handler.LastRequest.Headers.GetValues("Authorization").First());
    }

    /// <summary>
    /// Simple mock HTTP handler for testing.
    /// </summary>
    internal class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage LastRequest { get; private set; }
        public string LastRequestBody { get; private set; }
        public string ResponseContent { get; set; } = "{}";
        public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(ResponseStatusCode)
            {
                Content = new StringContent(ResponseContent, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
