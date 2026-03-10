using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.HubSpot.Models;

namespace HQ.Plugins.HubSpot;

public class HubSpotService
{
    private readonly HubSpotClient _client;
    private readonly LogDelegate _logger;

    public HubSpotService(ServiceConfig config, LogDelegate logger)
    {
        _logger = logger;
        _client = new HubSpotClient(config.BaseUrl, config.AccessToken);
    }

    // ───────────────────────────── Contacts ─────────────────────────────

    [Display(Name = "create_contact")]
    [Description("Create a new CRM contact (recruiter, partner, lead). Provide at least an email or name.")]
    [Parameters("""{"type":"object","properties":{"email":{"type":"string","description":"Contact email address"},"firstName":{"type":"string","description":"First name"},"lastName":{"type":"string","description":"Last name"},"company":{"type":"string","description":"Company name"},"jobTitle":{"type":"string","description":"Job title"},"phone":{"type":"string","description":"Phone number"},"linkedInUrl":{"type":"string","description":"LinkedIn profile URL"},"lifecycleStage":{"type":"string","description":"Lifecycle stage: subscriber, lead, opportunity, customer"}},"required":["email"]}""")]
    public async Task<object> CreateContact(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            throw new ArgumentException("Missing required parameter: email");

        var properties = new Dictionary<string, string>
        {
            ["email"] = request.Email
        };

        if (!string.IsNullOrWhiteSpace(request.FirstName))
            properties["firstname"] = request.FirstName;
        if (!string.IsNullOrWhiteSpace(request.LastName))
            properties["lastname"] = request.LastName;
        if (!string.IsNullOrWhiteSpace(request.Company))
            properties["company"] = request.Company;
        if (!string.IsNullOrWhiteSpace(request.JobTitle))
            properties["jobtitle"] = request.JobTitle;
        if (!string.IsNullOrWhiteSpace(request.Phone))
            properties["phone"] = request.Phone;
        if (!string.IsNullOrWhiteSpace(request.LinkedInUrl))
            properties["hs_linkedin_url"] = request.LinkedInUrl;
        if (!string.IsNullOrWhiteSpace(request.LifecycleStage))
            properties["lifecyclestage"] = request.LifecycleStage;

        var result = await _client.PostAsync("/crm/v3/objects/contacts", new { properties });

        return new
        {
            Success = true,
            ContactId = result.GetProperty("id").GetString(),
            Message = $"Contact created for {request.Email}"
        };
    }

    [Display(Name = "update_contact")]
    [Description("Update properties on an existing CRM contact by contact ID.")]
    [Parameters("""{"type":"object","properties":{"contactId":{"type":"string","description":"The HubSpot contact ID"},"email":{"type":"string","description":"Updated email address"},"firstName":{"type":"string","description":"Updated first name"},"lastName":{"type":"string","description":"Updated last name"},"company":{"type":"string","description":"Updated company name"},"jobTitle":{"type":"string","description":"Updated job title"},"phone":{"type":"string","description":"Updated phone number"},"linkedInUrl":{"type":"string","description":"Updated LinkedIn profile URL"},"lifecycleStage":{"type":"string","description":"Updated lifecycle stage: subscriber, lead, opportunity, customer"}},"required":["contactId"]}""")]
    public async Task<object> UpdateContact(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContactId))
            throw new ArgumentException("Missing required parameter: contactId");

        var properties = new Dictionary<string, string>();

        if (request.Email != null) properties["email"] = request.Email;
        if (request.FirstName != null) properties["firstname"] = request.FirstName;
        if (request.LastName != null) properties["lastname"] = request.LastName;
        if (request.Company != null) properties["company"] = request.Company;
        if (request.JobTitle != null) properties["jobtitle"] = request.JobTitle;
        if (request.Phone != null) properties["phone"] = request.Phone;
        if (request.LinkedInUrl != null) properties["hs_linkedin_url"] = request.LinkedInUrl;
        if (request.LifecycleStage != null) properties["lifecyclestage"] = request.LifecycleStage;

        if (properties.Count == 0)
            return new { Success = false, Message = "No properties to update" };

        await _client.PatchAsync($"/crm/v3/objects/contacts/{request.ContactId}", new { properties });

        return new { Success = true, Message = $"Contact {request.ContactId} updated" };
    }

    [Display(Name = "search_contacts")]
    [Description("Search CRM contacts by name, email, company, or custom query string.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Search query (searches across name, email, phone, company)"},"maxResults":{"type":"integer","description":"Maximum results to return (default 10, max 100)"},"properties":{"type":"string","description":"Comma-separated property names to return (default: firstname,lastname,email,company,jobtitle,lifecyclestage)"}},"required":["query"]}""")]
    public async Task<object> SearchContacts(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        var maxResults = Math.Min(request.MaxResults ?? 10, 100);
        var props = string.IsNullOrWhiteSpace(request.Properties)
            ? new[] { "firstname", "lastname", "email", "company", "jobtitle", "lifecyclestage", "hs_linkedin_url" }
            : request.Properties.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var body = new
        {
            query = request.Query,
            limit = maxResults,
            properties = props
        };

        var result = await _client.PostAsync("/crm/v3/objects/contacts/search", body);

        var contacts = new List<object>();
        if (result.TryGetProperty("results", out var results))
        {
            foreach (var contact in results.EnumerateArray())
            {
                var contactProps = contact.GetProperty("properties");
                contacts.Add(new
                {
                    Id = contact.GetProperty("id").GetString(),
                    FirstName = GetProp(contactProps, "firstname"),
                    LastName = GetProp(contactProps, "lastname"),
                    Email = GetProp(contactProps, "email"),
                    Company = GetProp(contactProps, "company"),
                    JobTitle = GetProp(contactProps, "jobtitle"),
                    LifecycleStage = GetProp(contactProps, "lifecyclestage"),
                    LinkedInUrl = GetProp(contactProps, "hs_linkedin_url")
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : contacts.Count;
        return new { Total = total, Contacts = contacts };
    }

    [Display(Name = "get_contact")]
    [Description("Get full details of a CRM contact by their contact ID.")]
    [Parameters("""{"type":"object","properties":{"contactId":{"type":"string","description":"The HubSpot contact ID"},"properties":{"type":"string","description":"Comma-separated property names to return (default: all standard properties)"}},"required":["contactId"]}""")]
    public async Task<object> GetContact(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContactId))
            throw new ArgumentException("Missing required parameter: contactId");

        var props = string.IsNullOrWhiteSpace(request.Properties)
            ? "firstname,lastname,email,company,jobtitle,phone,lifecyclestage,hs_linkedin_url,createdate,lastmodifieddate,notes_last_updated"
            : request.Properties;

        var result = await _client.GetAsync($"/crm/v3/objects/contacts/{request.ContactId}?properties={props}");

        var contactProps = result.GetProperty("properties");
        return new
        {
            Id = result.GetProperty("id").GetString(),
            FirstName = GetProp(contactProps, "firstname"),
            LastName = GetProp(contactProps, "lastname"),
            Email = GetProp(contactProps, "email"),
            Company = GetProp(contactProps, "company"),
            JobTitle = GetProp(contactProps, "jobtitle"),
            Phone = GetProp(contactProps, "phone"),
            LifecycleStage = GetProp(contactProps, "lifecyclestage"),
            LinkedInUrl = GetProp(contactProps, "hs_linkedin_url"),
            Created = GetProp(contactProps, "createdate"),
            LastModified = GetProp(contactProps, "lastmodifieddate")
        };
    }

    // ───────────────────────────── Deals ─────────────────────────────

    [Display(Name = "create_deal")]
    [Description("Create a deal (contract opportunity) in the CRM pipeline. Associate with a contact by providing contactId.")]
    [Parameters("""{"type":"object","properties":{"dealName":{"type":"string","description":"Name of the deal/opportunity"},"dealStage":{"type":"string","description":"Deal stage: appointmentscheduled, qualifiedtobuy, presentationscheduled, decisionmakerboughtin, contractsent, closedwon, closedlost"},"amount":{"type":"number","description":"Deal value in dollars"},"closeDate":{"type":"string","description":"Expected close date (YYYY-MM-DD)"},"pipeline":{"type":"string","description":"Pipeline name (default: 'default')"},"contactId":{"type":"string","description":"Contact ID to associate with this deal"}},"required":["dealName"]}""")]
    public async Task<object> CreateDeal(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DealName))
            throw new ArgumentException("Missing required parameter: dealName");

        var properties = new Dictionary<string, object>
        {
            ["dealname"] = request.DealName
        };

        if (!string.IsNullOrWhiteSpace(request.DealStage))
            properties["dealstage"] = request.DealStage;
        if (request.Amount.HasValue)
            properties["amount"] = request.Amount.Value.ToString("F2");
        if (!string.IsNullOrWhiteSpace(request.CloseDate))
            properties["closedate"] = request.CloseDate;
        if (!string.IsNullOrWhiteSpace(request.Pipeline))
            properties["pipeline"] = request.Pipeline;

        var body = new Dictionary<string, object> { ["properties"] = properties };

        if (!string.IsNullOrWhiteSpace(request.ContactId))
        {
            body["associations"] = new[]
            {
                new
                {
                    to = new { id = request.ContactId },
                    types = new[]
                    {
                        new { associationCategory = "HUBSPOT_DEFINED", associationTypeId = 3 }
                    }
                }
            };
        }

        var result = await _client.PostAsync("/crm/v3/objects/deals", body);

        return new
        {
            Success = true,
            DealId = result.GetProperty("id").GetString(),
            Message = $"Deal '{request.DealName}' created"
        };
    }

    [Display(Name = "update_deal")]
    [Description("Update an existing deal's stage, amount, close date, or other properties.")]
    [Parameters("""{"type":"object","properties":{"dealId":{"type":"string","description":"The HubSpot deal ID"},"dealName":{"type":"string","description":"Updated deal name"},"dealStage":{"type":"string","description":"Updated deal stage"},"amount":{"type":"number","description":"Updated deal value"},"closeDate":{"type":"string","description":"Updated close date (YYYY-MM-DD)"},"pipeline":{"type":"string","description":"Updated pipeline name"}},"required":["dealId"]}""")]
    public async Task<object> UpdateDeal(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DealId))
            throw new ArgumentException("Missing required parameter: dealId");

        var properties = new Dictionary<string, object>();

        if (request.DealName != null) properties["dealname"] = request.DealName;
        if (request.DealStage != null) properties["dealstage"] = request.DealStage;
        if (request.Amount.HasValue) properties["amount"] = request.Amount.Value.ToString("F2");
        if (request.CloseDate != null) properties["closedate"] = request.CloseDate;
        if (request.Pipeline != null) properties["pipeline"] = request.Pipeline;

        if (properties.Count == 0)
            return new { Success = false, Message = "No properties to update" };

        await _client.PatchAsync($"/crm/v3/objects/deals/{request.DealId}", new { properties });

        return new { Success = true, Message = $"Deal {request.DealId} updated" };
    }

    [Display(Name = "search_deals")]
    [Description("Search and filter deals in the CRM pipeline by name, stage, or amount.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Search query for deal names"},"dealStage":{"type":"string","description":"Filter by deal stage"},"pipeline":{"type":"string","description":"Filter by pipeline name"},"maxResults":{"type":"integer","description":"Maximum results to return (default 10, max 100)"}},"required":[]}""")]
    public async Task<object> SearchDeals(ServiceConfig config, ServiceRequest request)
    {
        var maxResults = Math.Min(request.MaxResults ?? 10, 100);
        var props = new[] { "dealname", "dealstage", "amount", "closedate", "pipeline", "createdate" };

        var filters = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.DealStage))
        {
            filters.Add(new { propertyName = "dealstage", @operator = "EQ", value = request.DealStage });
        }
        if (!string.IsNullOrWhiteSpace(request.Pipeline))
        {
            filters.Add(new { propertyName = "pipeline", @operator = "EQ", value = request.Pipeline });
        }

        var body = new Dictionary<string, object>
        {
            ["limit"] = maxResults,
            ["properties"] = props
        };

        if (!string.IsNullOrWhiteSpace(request.Query))
            body["query"] = request.Query;

        if (filters.Count > 0)
        {
            body["filterGroups"] = new[]
            {
                new { filters }
            };
        }

        var result = await _client.PostAsync("/crm/v3/objects/deals/search", body);

        var deals = new List<object>();
        if (result.TryGetProperty("results", out var results))
        {
            foreach (var deal in results.EnumerateArray())
            {
                var dealProps = deal.GetProperty("properties");
                deals.Add(new
                {
                    Id = deal.GetProperty("id").GetString(),
                    DealName = GetProp(dealProps, "dealname"),
                    DealStage = GetProp(dealProps, "dealstage"),
                    Amount = GetProp(dealProps, "amount"),
                    CloseDate = GetProp(dealProps, "closedate"),
                    Pipeline = GetProp(dealProps, "pipeline"),
                    Created = GetProp(dealProps, "createdate")
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : deals.Count;
        return new { Total = total, Deals = deals };
    }

    // ───────────────────────────── Companies ─────────────────────────────

    [Display(Name = "create_company")]
    [Description("Create a company record in the CRM.")]
    [Parameters("""{"type":"object","properties":{"companyName":{"type":"string","description":"Company name"},"domain":{"type":"string","description":"Company website domain (e.g. example.com)"},"industry":{"type":"string","description":"Industry vertical"}},"required":["companyName"]}""")]
    public async Task<object> CreateCompany(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CompanyName))
            throw new ArgumentException("Missing required parameter: companyName");

        var properties = new Dictionary<string, string>
        {
            ["name"] = request.CompanyName
        };

        if (!string.IsNullOrWhiteSpace(request.Domain))
            properties["domain"] = request.Domain;
        if (!string.IsNullOrWhiteSpace(request.Industry))
            properties["industry"] = request.Industry;

        var result = await _client.PostAsync("/crm/v3/objects/companies", new { properties });

        return new
        {
            Success = true,
            CompanyId = result.GetProperty("id").GetString(),
            Message = $"Company '{request.CompanyName}' created"
        };
    }

    [Display(Name = "search_companies")]
    [Description("Search companies in the CRM by name, domain, or industry.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Search query for company names"},"domain":{"type":"string","description":"Filter by domain"},"industry":{"type":"string","description":"Filter by industry"},"maxResults":{"type":"integer","description":"Maximum results to return (default 10, max 100)"}},"required":["query"]}""")]
    public async Task<object> SearchCompanies(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        var maxResults = Math.Min(request.MaxResults ?? 10, 100);
        var props = new[] { "name", "domain", "industry", "city", "state", "country", "createdate" };

        var result = await _client.PostAsync("/crm/v3/objects/companies/search", new
        {
            query = request.Query,
            limit = maxResults,
            properties = props
        });

        var companies = new List<object>();
        if (result.TryGetProperty("results", out var results))
        {
            foreach (var company in results.EnumerateArray())
            {
                var companyProps = company.GetProperty("properties");
                companies.Add(new
                {
                    Id = company.GetProperty("id").GetString(),
                    Name = GetProp(companyProps, "name"),
                    Domain = GetProp(companyProps, "domain"),
                    Industry = GetProp(companyProps, "industry"),
                    City = GetProp(companyProps, "city"),
                    State = GetProp(companyProps, "state"),
                    Country = GetProp(companyProps, "country"),
                    Created = GetProp(companyProps, "createdate")
                });
            }
        }

        var total = result.TryGetProperty("total", out var totalProp) ? totalProp.GetInt32() : companies.Count;
        return new { Total = total, Companies = companies };
    }

    // ───────────────────────────── Notes ─────────────────────────────

    [Display(Name = "add_note")]
    [Description("Add a note/activity to a contact, deal, or company in the CRM.")]
    [Parameters("""{"type":"object","properties":{"notes":{"type":"string","description":"The note content/body text"},"objectType":{"type":"string","description":"Object type to attach the note to: contacts, deals, or companies"},"objectId":{"type":"string","description":"The ID of the contact, deal, or company to attach the note to"}},"required":["notes","objectType","objectId"]}""")]
    public async Task<object> AddNote(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Notes))
            throw new ArgumentException("Missing required parameter: notes");
        if (string.IsNullOrWhiteSpace(request.ObjectType))
            throw new ArgumentException("Missing required parameter: objectType");
        if (string.IsNullOrWhiteSpace(request.ObjectId))
            throw new ArgumentException("Missing required parameter: objectId");

        var objectType = request.ObjectType.ToLowerInvariant();
        var associationTypeId = objectType switch
        {
            "contacts" => 202,
            "deals" => 214,
            "companies" => 190,
            _ => throw new ArgumentException($"Invalid objectType: {request.ObjectType}. Must be contacts, deals, or companies.")
        };

        var result = await _client.PostAsync("/crm/v3/objects/notes", new
        {
            properties = new Dictionary<string, string>
            {
                ["hs_note_body"] = request.Notes,
                ["hs_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()
            },
            associations = new[]
            {
                new
                {
                    to = new { id = request.ObjectId },
                    types = new[]
                    {
                        new { associationCategory = "HUBSPOT_DEFINED", associationTypeId }
                    }
                }
            }
        });

        return new
        {
            Success = true,
            NoteId = result.GetProperty("id").GetString(),
            Message = $"Note added to {objectType} {request.ObjectId}"
        };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static string GetProp(JsonElement properties, string name)
    {
        return properties.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
