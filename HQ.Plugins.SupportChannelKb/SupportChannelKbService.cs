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

    public async Task<string[]> SearchKnowledgeBase(SearchSupportChannelsArgs request)
    {
        using var httpClient = new HttpClient();

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
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await httpClient.GetAsync($"{_config.SupportChannelKbUrl}/collections");
        response.EnsureSuccessStatusCode();

        var collections = await response.Content.ReadFromJsonAsync<Collection[]>();
        return collections;
    }

    public async Task<object> AddCollection(AddSupportChannelCollectionArgs request)
    {
        using var httpClient = new HttpClient();
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

        return await response.Content.ReadFromJsonAsync<object>();
    }

    public async Task<object> AddTextToCollection(SaveSupportChannelInformationArgs request)
    {
        using var httpClient = new HttpClient();
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

        return await response.Content.ReadFromJsonAsync<object>();
    }

    public async Task<object> HealthCheck()
    {
        using var httpClient = new HttpClient();
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
        return await SearchKnowledgeBase(request);
    }

    [Display(Name = "get_support_channel_collections")]
    [Description("Retrieves a list of all available support channel collections")]
    [Parameters(typeof(EmptyArgs))]
    public async Task<object> GetSupportChannelCollections(ServiceConfig config, EmptyArgs request)
    {
        return await GetCollections();
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
