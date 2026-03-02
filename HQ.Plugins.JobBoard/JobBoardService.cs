using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.JobBoard.Clients;
using HQ.Plugins.JobBoard.Models;

namespace HQ.Plugins.JobBoard;

public class JobBoardService
{
    private readonly ServiceConfig _config;
    private readonly LogDelegate _logger;

    public JobBoardService(ServiceConfig config, LogDelegate logger)
    {
        _config = config;
        _logger = logger;
    }

    // ───────────────────────────── Job Search ─────────────────────────────

    [Display(Name = "search_jobs")]
    [Description("Search for contract/freelance jobs across configured platforms (Indeed, Upwork, LinkedIn, Toptal). Specify source='all' to search everywhere or filter by source name.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Search keywords (e.g. '.NET developer', 'React contract')"},"location":{"type":"string","description":"Job location filter"},"jobType":{"type":"string","description":"Job type: contract, freelance, full-time"},"source":{"type":"string","description":"Source to search: indeed, upwork, linkedin, toptal, or all (default: all)"},"maxResults":{"type":"integer","description":"Maximum results per source (default 10)"},"skills":{"type":"string","description":"Comma-separated skills to filter by"}},"required":["query"]}""")]
    public async Task<object> SearchJobs(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        var maxResults = request.MaxResults ?? 10;
        var source = (request.Source ?? "all").ToLowerInvariant();
        var allListings = new List<JobListing>();
        var errors = new List<string>();

        // Indeed
        if ((source == "all" || source == "indeed") && !string.IsNullOrWhiteSpace(config.IndeedApiKey))
        {
            try
            {
                using var client = new IndeedClient(config.IndeedApiKey, config.IndeedApiHost ?? "indeed12.p.rapidapi.com");
                var results = await client.SearchAsync(request.Query, request.Location, maxResults, request.JobType);
                allListings.AddRange(results);
            }
            catch (Exception ex)
            {
                errors.Add($"Indeed: {ex.Message}");
            }
        }

        // Upwork
        if ((source == "all" || source == "upwork") && !string.IsNullOrWhiteSpace(config.UpworkRssFeedUrl))
        {
            try
            {
                using var client = new UpworkClient(config.UpworkRssFeedUrl);
                var results = await client.SearchAsync(request.Query, maxResults, request.Skills);
                allListings.AddRange(results);
            }
            catch (Exception ex)
            {
                errors.Add($"Upwork: {ex.Message}");
            }
        }

        // LinkedIn Jobs
        if ((source == "all" || source == "linkedin") && !string.IsNullOrWhiteSpace(config.ProxycurlApiKey))
        {
            try
            {
                using var client = new LinkedInJobsClient(config.ProxycurlApiKey);
                var results = await client.SearchAsync(request.Query, request.Location, maxResults, request.JobType);
                allListings.AddRange(results);
            }
            catch (Exception ex)
            {
                errors.Add($"LinkedIn: {ex.Message}");
            }
        }

        // Toptal
        if ((source == "all" || source == "toptal") && config.EnableToptal)
        {
            try
            {
                using var client = new ToptalClient();
                var results = await client.SearchAsync(request.Query, maxResults);
                allListings.AddRange(results);
            }
            catch (Exception ex)
            {
                errors.Add($"Toptal: {ex.Message}");
            }
        }

        // Cache results for get_job_details
        await CacheJobListings(config, allListings);

        return new
        {
            Total = allListings.Count,
            Jobs = allListings.Select(j => new
            {
                j.Id,
                j.Title,
                j.Company,
                j.Location,
                j.Salary,
                j.JobType,
                j.Source,
                j.PostedDate,
                j.Url
            }),
            Errors = errors.Count > 0 ? errors : null
        };
    }

    [Display(Name = "get_job_details")]
    [Description("Get full details for a specific job listing by its ID (returned from search_jobs).")]
    [Parameters("""{"type":"object","properties":{"jobId":{"type":"string","description":"The job listing ID (e.g. indeed-a1b2c3d4)"}},"required":["jobId"]}""")]
    public async Task<object> GetJobDetails(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new ArgumentException("Missing required parameter: jobId");

        var cache = await LoadJobCache(config);

        if (cache.TryGetValue(request.JobId, out var listing))
        {
            return new
            {
                Success = true,
                listing.Id,
                listing.Title,
                listing.Company,
                listing.Location,
                listing.Description,
                listing.Salary,
                listing.JobType,
                listing.Url,
                listing.Source,
                listing.PostedDate,
                listing.Skills
            };
        }

        return new { Success = false, Message = $"Job '{request.JobId}' not found in cache. Run search_jobs first." };
    }

    // ───────────────────────────── Application Tracking ─────────────────────────────

    [Display(Name = "track_application")]
    [Description("Record that you've applied to a job. Creates a tracking entry with the job details and application date.")]
    [Parameters("""{"type":"object","properties":{"jobId":{"type":"string","description":"The job listing ID"},"notes":{"type":"string","description":"Notes about the application (e.g. cover letter summary, referral info)"}},"required":["jobId"]}""")]
    public async Task<object> TrackApplication(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new ArgumentException("Missing required parameter: jobId");

        var applications = await LoadApplications(config);
        var cache = await LoadJobCache(config);

        var appId = Guid.NewGuid().ToString("N")[..8];
        var jobTitle = cache.TryGetValue(request.JobId, out var listing) ? listing.Title : "Unknown";
        var company = listing?.Company ?? "Unknown";

        applications[appId] = new ApplicationEntry
        {
            Id = appId,
            JobId = request.JobId,
            JobTitle = jobTitle,
            Company = company,
            Status = "applied",
            Notes = request.Notes,
            AppliedAt = DateTime.UtcNow.ToString("o"),
            UpdatedAt = DateTime.UtcNow.ToString("o")
        };

        await SaveApplications(config, applications);

        return new
        {
            Success = true,
            ApplicationId = appId,
            Message = $"Application tracked for '{jobTitle}' at {company}"
        };
    }

    [Display(Name = "update_application")]
    [Description("Update the status of a tracked job application (e.g. interviewing, offered, rejected).")]
    [Parameters("""{"type":"object","properties":{"applicationId":{"type":"string","description":"The application tracking ID"},"status":{"type":"string","description":"New status: applied, interviewing, offered, rejected, withdrawn"},"notes":{"type":"string","description":"Additional notes about the status change"}},"required":["applicationId","status"]}""")]
    public async Task<object> UpdateApplication(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicationId))
            throw new ArgumentException("Missing required parameter: applicationId");
        if (string.IsNullOrWhiteSpace(request.Status))
            throw new ArgumentException("Missing required parameter: status");

        var applications = await LoadApplications(config);

        if (!applications.TryGetValue(request.ApplicationId, out var app))
            return new { Success = false, Message = $"Application '{request.ApplicationId}' not found" };

        app = app with
        {
            Status = request.Status.ToLowerInvariant(),
            Notes = request.Notes ?? app.Notes,
            UpdatedAt = DateTime.UtcNow.ToString("o")
        };
        applications[request.ApplicationId] = app;

        await SaveApplications(config, applications);

        return new { Success = true, Message = $"Application {request.ApplicationId} updated to '{request.Status}'" };
    }

    [Display(Name = "get_applications")]
    [Description("List all tracked job applications with their current status.")]
    [Parameters("""{"type":"object","properties":{"status":{"type":"string","description":"Filter by status: applied, interviewing, offered, rejected, withdrawn"}},"required":[]}""")]
    public async Task<object> GetApplications(ServiceConfig config, ServiceRequest request)
    {
        var applications = await LoadApplications(config);

        var results = applications.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(request.Status))
            results = results.Where(a => a.Status.Equals(request.Status, StringComparison.OrdinalIgnoreCase));

        var list = results.OrderByDescending(a => a.UpdatedAt).ToList();

        return new { Total = list.Count, Applications = list };
    }

    [Display(Name = "get_job_summary")]
    [Description("Get a summary of new relevant job listings since the last check. Returns counts by source and highlights high-value matches.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Search keywords to find relevant jobs"},"maxResults":{"type":"integer","description":"Maximum results per source (default 5)"}},"required":["query"]}""")]
    public async Task<object> GetJobSummary(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        // Perform a fresh search across all configured sources
        var searchRequest = new ServiceRequest
        {
            Query = request.Query,
            MaxResults = request.MaxResults ?? 5,
            Source = "all"
        };

        var searchResult = await SearchJobs(config, searchRequest);

        // Load existing applications to exclude already-applied jobs
        var applications = await LoadApplications(config);
        var appliedJobIds = new HashSet<string>(applications.Values.Select(a => a.JobId));

        var cache = await LoadJobCache(config);
        var newJobs = cache.Values
            .Where(j => !appliedJobIds.Contains(j.Id))
            .ToList();

        var bySource = newJobs.GroupBy(j => j.Source)
            .ToDictionary(g => g.Key, g => g.Count());

        return new
        {
            TotalNew = newJobs.Count,
            AlreadyApplied = appliedJobIds.Count,
            BySource = bySource,
            TopListings = newJobs.Take(10).Select(j => new
            {
                j.Id,
                j.Title,
                j.Company,
                j.Location,
                j.Source,
                j.PostedDate
            })
        };
    }

    // ───────────────────────────── Persistence ─────────────────────────────

    private static string GetDataDir(ServiceConfig config)
    {
        var dir = config.DataDirectory ?? Path.Combine(Path.GetTempPath(), "hq-jobboard");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task CacheJobListings(ServiceConfig config, List<JobListing> listings)
    {
        var path = Path.Combine(GetDataDir(config), "job-cache.json");
        var cache = await LoadJobCache(config);
        foreach (var listing in listings)
        {
            cache[listing.Id] = listing;
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<Dictionary<string, JobListing>> LoadJobCache(ServiceConfig config)
    {
        var path = Path.Combine(GetDataDir(config), "job-cache.json");
        if (!File.Exists(path)) return new Dictionary<string, JobListing>();
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, JobListing>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, JobListing>();
    }

    private static async Task<Dictionary<string, ApplicationEntry>> LoadApplications(ServiceConfig config)
    {
        var path = Path.Combine(GetDataDir(config), "applications.json");
        if (!File.Exists(path)) return new Dictionary<string, ApplicationEntry>();
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, ApplicationEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, ApplicationEntry>();
    }

    private static async Task SaveApplications(ServiceConfig config, Dictionary<string, ApplicationEntry> applications)
    {
        var path = Path.Combine(GetDataDir(config), "applications.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(applications, new JsonSerializerOptions { WriteIndented = true }));
    }

    private record ApplicationEntry
    {
        public string Id { get; init; }
        public string JobId { get; init; }
        public string JobTitle { get; init; }
        public string Company { get; init; }
        public string Status { get; init; }
        public string Notes { get; init; }
        public string AppliedAt { get; init; }
        public string UpdatedAt { get; init; }
    }
}
