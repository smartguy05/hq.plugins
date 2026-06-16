using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Perplexity;

/// <summary>
/// Result of a Perplexity completion: the synthesized answer plus source citations.
/// </summary>
public record PerplexityResult(string Answer, List<string> Citations);

/// <summary>
/// Wrapper over Perplexity's APIs:
/// - synchronous /chat/completions (used by perplexity_search)
/// - the async job API /v1/async/sonar (submit + poll, used by perplexity_deep_research)
/// </summary>
public static class PerplexityClient
{
    private const string ChatEndpoint = "https://api.perplexity.ai/chat/completions";
    private const string AsyncEndpoint = "https://api.perplexity.ai/v1/async/sonar";

    /// <summary>
    /// Synchronous completion — blocks until the model responds. Suitable for fast models.
    /// </summary>
    public static async Task<PerplexityResult> ResearchAsync(
        string apiKey,
        string model,
        string query,
        string recency,
        IEnumerable<string> domainFilters,
        int? maxTokens,
        TimeSpan timeout)
    {
        using var httpClient = CreateClient(apiKey, timeout);
        var body = BuildRequestBody(model, query, recency, domainFilters, maxTokens);

        var response = await httpClient.PostAsJsonAsync(ChatEndpoint, body);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return ParseCompletion(doc.RootElement);
    }

    /// <summary>
    /// Runs a deep-research job via the async job API: submits the job, then polls until it completes,
    /// fails, or the overall deadline elapses. Each HTTP call is short; the wait happens between polls.
    /// </summary>
    public static async Task<PerplexityResult> RunDeepResearchAsync(
        string apiKey,
        string query,
        string recency,
        IEnumerable<string> domainFilters,
        int? maxTokens,
        TimeSpan pollInterval,
        TimeSpan maxWait,
        CancellationToken cancellationToken = default)
    {
        var requestId = await SubmitDeepResearchAsync(apiKey, query, recency, domainFilters, maxTokens);

        var deadline = DateTimeOffset.UtcNow + maxWait;
        while (true)
        {
            await Task.Delay(pollInterval, cancellationToken);

            var (status, result, error) = await PollDeepResearchAsync(apiKey, requestId);
            switch (status)
            {
                case "COMPLETED" when result is not null:
                    return result;
                case "FAILED":
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error) ? "Deep research job failed." : error);
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Deep research job {requestId} did not complete within {maxWait.TotalMinutes:0} minutes (last status: {status}).");
            }
        }
    }

    /// <summary>Submits a sonar-deep-research job and returns its request id.</summary>
    private static async Task<string> SubmitDeepResearchAsync(
        string apiKey, string query, string recency, IEnumerable<string> domainFilters, int? maxTokens)
    {
        using var httpClient = CreateClient(apiKey, TimeSpan.FromSeconds(60));
        var payload = new Dictionary<string, object>
        {
            ["request"] = BuildRequestBody("sonar-deep-research", query, recency, domainFilters, maxTokens)
        };

        var response = await httpClient.PostAsJsonAsync(AsyncEndpoint, payload);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!doc.RootElement.TryGetProperty("id", out var idProp) || idProp.GetString() is not { } id)
        {
            throw new InvalidOperationException("Async submit response did not contain a request id.");
        }
        return id;
    }

    /// <summary>Polls a single async job; returns its status and, when COMPLETED, the parsed result.</summary>
    private static async Task<(string Status, PerplexityResult Result, string Error)> PollDeepResearchAsync(
        string apiKey, string requestId)
    {
        using var httpClient = CreateClient(apiKey, TimeSpan.FromSeconds(60));

        var response = await httpClient.GetAsync($"{AsyncEndpoint}/{requestId}");
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        var error = root.TryGetProperty("error_message", out var errProp) ? errProp.GetString() : null;

        PerplexityResult result = null;
        if (status == "COMPLETED" && root.TryGetProperty("response", out var resp) &&
            resp.ValueKind == JsonValueKind.Object)
        {
            result = ParseCompletion(resp);
        }

        return (status, result, error);
    }

    private static HttpClient CreateClient(string apiKey, TimeSpan timeout)
    {
        var httpClient = new HttpClient { Timeout = timeout };
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpClient;
    }

    private static Dictionary<string, object> BuildRequestBody(
        string model, string query, string recency, IEnumerable<string> domainFilters, int? maxTokens)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new { role = "user", content = query }
            }
        };

        if (!string.IsNullOrWhiteSpace(recency))
        {
            body["search_recency_filter"] = recency;
        }

        var domains = domainFilters?.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct().ToList();
        if (domains is { Count: > 0 })
        {
            body["search_domain_filter"] = domains;
        }

        if (maxTokens is > 0)
        {
            body["max_tokens"] = maxTokens.Value;
        }

        return body;
    }

    /// <summary>
    /// Parses a completion object (the /chat/completions root, or the async job's "response" object)
    /// into an answer + citations.
    /// </summary>
    private static PerplexityResult ParseCompletion(JsonElement element)
    {
        var answer = string.Empty;
        if (element.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message");
            if (message.TryGetProperty("content", out var content))
            {
                answer = content.GetString() ?? string.Empty;
            }
        }

        return new PerplexityResult(answer, ExtractCitations(element));
    }

    /// <summary>
    /// Sources arrive either as a "citations" array of URL strings, or as a "search_results" array of
    /// objects with a "url" (and "title"). Handle both.
    /// </summary>
    private static List<string> ExtractCitations(JsonElement element)
    {
        var citations = new List<string>();

        if (element.TryGetProperty("citations", out var cites) && cites.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cites.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.String)
                {
                    var url = c.GetString();
                    if (!string.IsNullOrWhiteSpace(url)) citations.Add(url);
                }
            }
        }

        if (citations.Count == 0 &&
            element.TryGetProperty("search_results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in results.EnumerateArray())
            {
                if (!r.TryGetProperty("url", out var urlProp)) continue;
                var url = urlProp.GetString();
                if (string.IsNullOrWhiteSpace(url)) continue;

                var title = r.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
                citations.Add(string.IsNullOrWhiteSpace(title) ? url : $"{title} — {url}");
            }
        }

        return citations;
    }
}
