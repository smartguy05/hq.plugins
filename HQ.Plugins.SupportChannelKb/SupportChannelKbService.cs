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

    public async Task<string[]> SearchKnowledgeBase(ServiceRequest request)
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

    public async Task<object> AddCollection(ServiceRequest request)
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

    public async Task<object> AddTextToCollection(ServiceRequest request)
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
    [Parameters("""{"type":"object","properties":{"searchCriteria":{"type":"string","description":"The search text to find relevant knowledge base entries"}},"required":["searchCriteria"]}""")]
    public async Task<object> SearchSupportChannels(ServiceConfig config, ServiceRequest request)
    {
        return await SearchKnowledgeBase(request);
    }

    [Display(Name = "get_support_channel_collections")]
    [Description("Retrieves a list of all available support channel collections")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> GetSupportChannelCollections(ServiceConfig config, ServiceRequest request)
    {
        return await GetCollections();
    }

    [Display(Name = "add_support_channel_collection")]
    [Description("Creates a new support channel collection with a name and description")]
    [Parameters("""{"type":"object","properties":{"supportChannel":{"type":"string","description":"The name of the new collection to create"},"description":{"type":"string","description":"A description of the collection"}},"required":["supportChannel"]}""")]
    public async Task<object> AddSupportChannelCollection(ServiceConfig config, ServiceRequest request)
    {
        return await AddCollection(request);
    }

    [Display(Name = "save_support_channel_information")]
    [Description("Saves new information/text to an existing support channel collection")]
    [Parameters("""{"type":"object","properties":{"newInformation":{"type":"string","description":"The new information text to save to the collection"},"description":{"type":"string","description":"A description or context for the information"},"newInformationMetaData":{"type":"array","description":"Optional metadata key-value pairs for the information","items":{"type":"object"}}},"required":["newInformation"]}""")]
    public async Task<object> SaveSupportChannelInformation(ServiceConfig config, ServiceRequest request)
    {
        return await AddTextToCollection(request);
    }

    [Display(Name = "support_channel_health_check")]
    [Description("Checks the health status of the support channel knowledge base service")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> SupportChannelHealthCheck(ServiceConfig config, ServiceRequest request)
    {
        return await HealthCheck();
    }
}