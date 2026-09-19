using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HQ.Models.Helpers;
using HQ.Plugins.SupportChannelKb.Models;

namespace HQ.Plugins.SupportChannelKb;

public class SupportChannelKbService
{
    private readonly ServiceConfig _config;

    public SupportChannelKbService(ServiceConfig  config)
    {
        _config = config;
    }

    // Seam for tests — injects a fake HttpMessageHandler so tool bodies can run without a network.
    internal HttpMessageHandler HttpHandler { get; set; }

    // SAFE-02: KB/article/message content comes from external support channels and is untrusted
    // inbound content — wrap it as Untrusted for host prompt-injection screening.
    private static object AsUntrusted(string content, string provenance, string source) =>
        string.IsNullOrEmpty(content) ? content : new HQ.Models.Safety.Untrusted<string>(content, provenance, source);

    public async Task<string[]> SearchKnowledgeBase(SearchSupportChannelsArgs request)
    {
        using var httpClient = HttpHandler != null ? new HttpClient(HttpHandler, disposeHandler: false) : new HttpClient();

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _config.DefaultChannelApiKey);
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var requestBody = new { text = request.SearchCriteria };

        var response = await httpClient.PostAsJsonAsync(
            $"{_config.SupportChannelKbUrl}/search/{_config.DefaultSaveChannel}",
            requestBody
        );

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<string[]>();
    }

    public async Task<object> GetCollections()
    {
        using var httpClient = HttpHandler != null ? new HttpClient(HttpHandler, disposeHandler: false) : new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.GetAsync($"{_config.SupportChannelKbUrl}/collections");
        response.EnsureSuccessStatusCode();

        // WP6B-12: the upstream response carries a per-collection api_key that nothing in this
        // plugin reads (auth uses _config.DefaultChannelApiKey) — deserializing straight into the
        // Collection DTO and returning it verbatim would leak that key into the LLM conversation
        // and the persisted trace/debug log. Collection has no api_key-shaped property, so this
        // projection is redaction-by-construction rather than an allow-list that can drift.
        var collections = await response.Content.ReadFromJsonAsync<Collection[]>();
        return collections;
    }

    public async Task<object> AddCollection(AddSupportChannelCollectionArgs request)
    {
        using var httpClient = HttpHandler != null ? new HttpClient(HttpHandler, disposeHandler: false) : new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var requestBody = new
        {
            name = request.SupportChannel,
            description = request.Description
        };

        var response = await httpClient.PostAsJsonAsync(
            $"{_config.SupportChannelKbUrl}/collections",
            requestBody
        );

        response.EnsureSuccessStatusCode();

        // WP6B-14: unconstrained third-party JSON from the KB service — wrap the whole response
        // wholesale (same treatment as get_support_channel_collections / search results) rather
        // than returning it verbatim, since the upstream service could reflect back fields we
        // don't control (WP6B-12 showed it already does this for /collections).
        var raw = await response.Content.ReadAsStringAsync();
        return AsUntrusted(raw, "supportkb-api-response", _config.SupportChannelKbUrl ?? "unknown");
    }

    public async Task<object> AddTextToCollection(SaveSupportChannelInformationArgs request)
    {
        using var httpClient = HttpHandler != null ? new HttpClient(HttpHandler, disposeHandler: false) : new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var requestBody = new
        {
            text = request.NewInformation,
            data = request.Description,
            metaData = request.NewInformationMetaData
        };

        var response = await httpClient.PostAsJsonAsync(
            $"{_config.SupportChannelKbUrl}/text/{_config.DefaultSaveChannel}",
            requestBody
        );

        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsStringAsync();
        return AsUntrusted(raw, "supportkb-api-response", _config.SupportChannelKbUrl ?? "unknown");
    }

    public async Task<object> HealthCheck()
    {
        using var httpClient = HttpHandler != null ? new HttpClient(HttpHandler, disposeHandler: false) : new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.GetAsync($"{_config.SupportChannelKbUrl}/healthcheck");
        response.EnsureSuccessStatusCode();

        return response.Content.ReadFromJsonAsync<dynamic>();
    }

    // --- Annotated wrapper methods for tool definition scanning ---

    [Display(Name = "search_support_channels")]
    [Description("Searches the support channel knowledge base for relevant information based on search criteria")]
    [Parameters(typeof(SearchSupportChannelsArgs))]
    public async Task<object> SearchSupportChannels(ServiceConfig config, SearchSupportChannelsArgs request)
    {
        var results = await SearchKnowledgeBase(request);
        if (results == null) return results;
        var source = string.IsNullOrEmpty(_config.DefaultSaveChannel) ? "unknown" : _config.DefaultSaveChannel;
        return results.Select(r => AsUntrusted(r, "supportkb-content", source)).ToArray();
    }

    [Display(Name = "get_support_channel_collections")]
    [Description("Retrieves a list of all available support channel collections")]
    [Parameters(typeof(EmptyArgs))]
    public async Task<object> GetSupportChannelCollections(ServiceConfig config, EmptyArgs request)
    {
        var collections = await GetCollections();

        // WP6B-14: name/description are set via add_support_channel_collection and can carry
        // attacker-authored text the same way KB search results can — wrap them like
        // SearchSupportChannels already wraps its results. Created is a server-set timestamp.
        if (collections is Collection[] typed)
        {
            return typed.Select(c => new
            {
                Name = AsUntrusted(c.Name, "supportkb-collection-name", "unknown"),
                Description = AsUntrusted(c.Description, "supportkb-collection-description", "unknown"),
                c.Created
            }).ToArray();
        }

        return collections;
    }

    [Display(Name = "add_support_channel_collection")]
    [Description("Creates a new support channel collection with a name and description")]
    [Parameters(typeof(AddSupportChannelCollectionArgs))]
    public async Task<object> AddSupportChannelCollection(ServiceConfig config, AddSupportChannelCollectionArgs request)
    {
        return await AddCollection(request);
    }

    [Display(Name = "save_support_channel_information")]
    [Description("Saves new information/text to an existing support channel collection")]
    [Parameters(typeof(SaveSupportChannelInformationArgs))]
    public async Task<object> SaveSupportChannelInformation(ServiceConfig config, SaveSupportChannelInformationArgs request)
    {
        return await AddTextToCollection(request);
    }

    [Display(Name = "support_channel_health_check")]
    [Description("Checks the health status of the support channel knowledge base service")]
    [Parameters(typeof(EmptyArgs))]
    public async Task<object> SupportChannelHealthCheck(ServiceConfig config, EmptyArgs request)
    {
        return await HealthCheck();
    }
}
