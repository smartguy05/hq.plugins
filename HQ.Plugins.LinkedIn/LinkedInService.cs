using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.LinkedIn;

public class LinkedInService
{
    private readonly LinkedInClient _linkedIn;
    private readonly ProxycurlClient _proxycurl;
    private readonly LogDelegate _logger;

    public LinkedInService(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _linkedIn = new LinkedInClient(config.LinkedInAccessToken, config.LinkedInPersonUrn);
        _proxycurl = new ProxycurlClient(config.ProxycurlBaseUrl, config.ProxycurlApiKey);
    }

    // ───────────────────────────── Posts ─────────────────────────────

    [Display(Name = "create_post")]
    [Description("Publish a text post to LinkedIn. Optionally include an article link. Visibility defaults to PUBLIC.")]
    [Parameters("""{"type":"object","properties":{"content":{"type":"string","description":"The post text content"},"mediaUrl":{"type":"string","description":"Optional URL to include as an article/link attachment"},"visibility":{"type":"string","description":"Post visibility: PUBLIC or CONNECTIONS (default: PUBLIC)"}},"required":["content"]}""")]
    public async Task<object> CreatePost(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Missing required parameter: content");

        var visibility = string.IsNullOrWhiteSpace(request.Visibility) ? "PUBLIC" : request.Visibility.ToUpperInvariant();

        var postBody = new Dictionary<string, object>
        {
            ["author"] = _linkedIn.PersonUrn,
            ["lifecycleState"] = "PUBLISHED",
            ["visibility"] = visibility,
            ["commentary"] = request.Content,
            ["distribution"] = new
            {
                feedDistribution = "MAIN_FEED",
                targetEntities = Array.Empty<object>(),
                thirdPartyDistributionChannels = Array.Empty<object>()
            }
        };

        if (!string.IsNullOrWhiteSpace(request.MediaUrl))
        {
            postBody["content"] = new
            {
                article = new
                {
                    source = request.MediaUrl,
                    title = request.Content.Length > 100 ? request.Content[..100] : request.Content
                }
            };
        }

        var result = await _linkedIn.PostAsync("/rest/posts", postBody);

        return new
        {
            Success = true,
            Message = "Post published to LinkedIn"
        };
    }

    [Display(Name = "get_profile")]
    [Description("Get your own LinkedIn profile summary including name, headline, and vanity name.")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> GetProfile(ServiceConfig config, ServiceRequest request)
    {
        var result = await _linkedIn.GetAsync("/rest/me?projection=(id,firstName,lastName,profilePicture,headline,vanityName)");

        return new
        {
            Id = GetStringOrNull(result, "id"),
            FirstName = GetLocalizedField(result, "firstName"),
            LastName = GetLocalizedField(result, "lastName"),
            Headline = GetLocalizedField(result, "headline"),
            VanityName = GetStringOrNull(result, "vanityName")
        };
    }

    [Display(Name = "get_posts")]
    [Description("Get your recent LinkedIn posts and their engagement metrics.")]
    [Parameters("""{"type":"object","properties":{"maxResults":{"type":"integer","description":"Maximum number of posts to return (default 10)"}},"required":[]}""")]
    public async Task<object> GetPosts(ServiceConfig config, ServiceRequest request)
    {
        var maxResults = request.MaxResults ?? 10;
        var personUrn = Uri.EscapeDataString(_linkedIn.PersonUrn);
        var result = await _linkedIn.GetAsync($"/rest/posts?author={personUrn}&q=author&count={maxResults}&sortBy=LAST_MODIFIED");

        var posts = new List<object>();
        if (result.TryGetProperty("elements", out var elements))
        {
            foreach (var post in elements.EnumerateArray())
            {
                posts.Add(new
                {
                    Urn = GetStringOrNull(post, "id"),
                    Commentary = GetStringOrNull(post, "commentary"),
                    Visibility = GetStringOrNull(post, "visibility"),
                    LifecycleState = GetStringOrNull(post, "lifecycleState"),
                    Created = post.TryGetProperty("createdAt", out var created) ? created.GetInt64() : 0,
                    LastModified = post.TryGetProperty("lastModifiedAt", out var modified) ? modified.GetInt64() : 0
                });
            }
        }

        return new { Posts = posts };
    }

    [Display(Name = "delete_post")]
    [Description("Delete a LinkedIn post by its URN.")]
    [Parameters("""{"type":"object","properties":{"postUrn":{"type":"string","description":"The post URN to delete (e.g. urn:li:share:1234567890)"}},"required":["postUrn"]}""")]
    public async Task<object> DeletePost(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PostUrn))
            throw new ArgumentException("Missing required parameter: postUrn");

        var encodedUrn = Uri.EscapeDataString(request.PostUrn);
        await _linkedIn.DeleteAsync($"/rest/posts/{encodedUrn}");

        return new { Success = true, Message = $"Post {request.PostUrn} deleted" };
    }

    // ───────────────────────────── Proxycurl Enrichment ─────────────────────────────

    [Display(Name = "lookup_person")]
    [Description("Enrich a LinkedIn profile URL with full details including experience, education, and skills. Uses Proxycurl (~$0.01 per call).")]
    [Parameters("""{"type":"object","properties":{"linkedInProfileUrl":{"type":"string","description":"Full LinkedIn profile URL (e.g. https://www.linkedin.com/in/username)"}},"required":["linkedInProfileUrl"]}""")]
    public async Task<object> LookupPerson(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.LinkedInProfileUrl))
            throw new ArgumentException("Missing required parameter: linkedInProfileUrl");

        var encodedUrl = Uri.EscapeDataString(request.LinkedInProfileUrl);
        var result = await _proxycurl.GetAsync($"/v2/linkedin?url={encodedUrl}");

        var experiences = new List<object>();
        if (result.TryGetProperty("experiences", out var expArray) && expArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var exp in expArray.EnumerateArray())
            {
                experiences.Add(new
                {
                    Title = GetStringOrNull(exp, "title"),
                    Company = GetStringOrNull(exp, "company"),
                    Location = GetStringOrNull(exp, "location"),
                    Description = GetStringOrNull(exp, "description"),
                    StartDate = GetStringOrNull(exp, "starts_at"),
                    EndDate = GetStringOrNull(exp, "ends_at")
                });
            }
        }

        return new
        {
            FullName = GetStringOrNull(result, "full_name"),
            Headline = GetStringOrNull(result, "headline"),
            Summary = GetStringOrNull(result, "summary"),
            Location = GetStringOrNull(result, "city") + ", " + GetStringOrNull(result, "country_full_name"),
            Connections = result.TryGetProperty("connections", out var conn) ? conn.GetInt32() : 0,
            Experiences = experiences
        };
    }

    [Display(Name = "search_people")]
    [Description("Search for people on LinkedIn by keyword, role, company, or location. Uses Proxycurl (~$0.01 per result). Use sparingly for cost efficiency.")]
    [Parameters("""{"type":"object","properties":{"keyword":{"type":"string","description":"Search keyword"},"currentCompany":{"type":"string","description":"Filter by current company name"},"currentRole":{"type":"string","description":"Filter by current role/title"},"location":{"type":"string","description":"Filter by location/region"},"industry":{"type":"string","description":"Filter by industry"},"maxResults":{"type":"integer","description":"Maximum results (default 10, each result costs ~$0.01)"}},"required":["keyword"]}""")]
    public async Task<object> SearchPeople(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
            throw new ArgumentException("Missing required parameter: keyword");

        var maxResults = request.MaxResults ?? 10;
        var queryParams = new List<string>
        {
            $"keyword={Uri.EscapeDataString(request.Keyword)}",
            $"page_size={maxResults}"
        };

        if (!string.IsNullOrWhiteSpace(request.CurrentCompany))
            queryParams.Add($"current_company_name={Uri.EscapeDataString(request.CurrentCompany)}");
        if (!string.IsNullOrWhiteSpace(request.CurrentRole))
            queryParams.Add($"current_role_title={Uri.EscapeDataString(request.CurrentRole)}");
        if (!string.IsNullOrWhiteSpace(request.Location))
            queryParams.Add($"region={Uri.EscapeDataString(request.Location)}");
        if (!string.IsNullOrWhiteSpace(request.Industry))
            queryParams.Add($"industry={Uri.EscapeDataString(request.Industry)}");

        var result = await _proxycurl.GetAsync($"/search/person/?{string.Join("&", queryParams)}");

        var people = new List<object>();
        if (result.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var person in results.EnumerateArray())
            {
                people.Add(new
                {
                    LinkedInUrl = GetStringOrNull(person, "linkedin_profile_url"),
                    Name = GetStringOrNull(person, "name"),
                    Headline = GetStringOrNull(person, "headline"),
                    Location = GetStringOrNull(person, "location")
                });
            }
        }

        var total = result.TryGetProperty("total_result_count", out var totalProp) ? totalProp.GetInt32() : people.Count;
        return new { Total = total, People = people };
    }

    [Display(Name = "lookup_company")]
    [Description("Get company details from a LinkedIn company URL. Uses Proxycurl (~$0.01 per call).")]
    [Parameters("""{"type":"object","properties":{"companyLinkedInUrl":{"type":"string","description":"Full LinkedIn company URL (e.g. https://www.linkedin.com/company/example)"}},"required":["companyLinkedInUrl"]}""")]
    public async Task<object> LookupCompany(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyLinkedInUrl))
            throw new ArgumentException("Missing required parameter: companyLinkedInUrl");

        var encodedUrl = Uri.EscapeDataString(request.CompanyLinkedInUrl);
        var result = await _proxycurl.GetAsync($"/linkedin/company?url={encodedUrl}");

        return new
        {
            Name = GetStringOrNull(result, "name"),
            Description = GetStringOrNull(result, "description"),
            Website = GetStringOrNull(result, "website"),
            Industry = GetStringOrNull(result, "industry"),
            CompanySize = GetStringOrNull(result, "company_size_on_linkedin"),
            Headquarters = GetStringOrNull(result, "hq") != null ? GetStringOrNull(result, "hq") : GetStringOrNull(result, "headquarters"),
            Founded = result.TryGetProperty("founded_year", out var founded) && founded.ValueKind == JsonValueKind.Number ? founded.GetInt32() : 0,
            Specialties = GetStringOrNull(result, "specialities"),
            LinkedInUrl = request.CompanyLinkedInUrl
        };
    }

    [Display(Name = "search_companies")]
    [Description("Search for companies on LinkedIn by keyword, industry, or location. Uses Proxycurl (~$0.01 per result).")]
    [Parameters("""{"type":"object","properties":{"keyword":{"type":"string","description":"Search keyword for company names"},"industry":{"type":"string","description":"Filter by industry"},"location":{"type":"string","description":"Filter by location/region"},"maxResults":{"type":"integer","description":"Maximum results (default 10, each result costs ~$0.01)"}},"required":["keyword"]}""")]
    public async Task<object> SearchCompanies(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Keyword))
            throw new ArgumentException("Missing required parameter: keyword");

        var maxResults = request.MaxResults ?? 10;
        var queryParams = new List<string>
        {
            $"keyword={Uri.EscapeDataString(request.Keyword)}",
            $"page_size={maxResults}"
        };

        if (!string.IsNullOrWhiteSpace(request.Industry))
            queryParams.Add($"industry={Uri.EscapeDataString(request.Industry)}");
        if (!string.IsNullOrWhiteSpace(request.Location))
            queryParams.Add($"region={Uri.EscapeDataString(request.Location)}");

        var result = await _proxycurl.GetAsync($"/search/company/?{string.Join("&", queryParams)}");

        var companies = new List<object>();
        if (result.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var company in results.EnumerateArray())
            {
                companies.Add(new
                {
                    LinkedInUrl = GetStringOrNull(company, "linkedin_profile_url"),
                    Name = GetStringOrNull(company, "name"),
                    Description = GetStringOrNull(company, "description"),
                    Industry = GetStringOrNull(company, "industry"),
                    Location = GetStringOrNull(company, "location")
                });
            }
        }

        var total = result.TryGetProperty("total_result_count", out var totalProp) ? totalProp.GetInt32() : companies.Count;
        return new { Total = total, Companies = companies };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static string GetStringOrNull(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string GetLocalizedField(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var field))
            return null;

        if (field.TryGetProperty("localized", out var localized))
        {
            foreach (var locale in localized.EnumerateObject())
            {
                return locale.Value.GetString();
            }
        }

        return null;
    }
}
