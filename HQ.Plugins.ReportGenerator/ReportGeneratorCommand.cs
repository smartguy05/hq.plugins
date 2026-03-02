using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.ReportGenerator.Models;
using Markdig;

namespace HQ.Plugins.ReportGenerator;

public class ReportGeneratorCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Report Generator";
    public override string Description => "Generate formatted reports from markdown content as PDF, HTML, or Markdown files";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    [Display(Name = "generate_report")]
    [Description("Generate a formatted report from markdown content. Outputs HTML or Markdown file to the configured output directory.")]
    [Parameters("""{"type":"object","properties":{"title":{"type":"string","description":"Report title"},"content":{"type":"string","description":"Report content in Markdown format"},"format":{"type":"string","description":"Output format: html or markdown (default: html)"},"fileName":{"type":"string","description":"Output filename without extension (auto-generated from title and date if empty)"}},"required":["title","content"]}""")]
    public async Task<object> GenerateReport(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Missing required parameter: title");
        if (string.IsNullOrWhiteSpace(request.Content))
            throw new ArgumentException("Missing required parameter: content");

        var outputDir = config.OutputDirectory ?? Path.Combine(Path.GetTempPath(), "hq-reports");
        Directory.CreateDirectory(outputDir);

        var format = (request.Format ?? "html").ToLowerInvariant();
        var baseName = string.IsNullOrWhiteSpace(request.FileName)
            ? $"{DateTime.Now:yyyy-MM-dd}_{SanitizeFileName(request.Title)}"
            : SanitizeFileName(request.FileName);

        string filePath;
        string outputContent;

        switch (format)
        {
            case "html":
                filePath = Path.Combine(outputDir, $"{baseName}.html");
                var pipeline = new MarkdownPipelineBuilder()
                    .UseAdvancedExtensions()
                    .Build();
                var htmlBody = Markdown.ToHtml(request.Content, pipeline);
                outputContent = WrapInHtmlTemplate(request.Title, htmlBody);
                break;

            case "markdown":
            case "md":
                filePath = Path.Combine(outputDir, $"{baseName}.md");
                outputContent = $"# {request.Title}\n\n{request.Content}";
                break;

            default:
                throw new ArgumentException($"Unsupported format: {format}. Use 'html' or 'markdown'.");
        }

        await File.WriteAllTextAsync(filePath, outputContent);

        // Save metadata for list_reports / get_report
        var metadataPath = Path.Combine(outputDir, ".report-index.json");
        var index = await LoadReportIndex(metadataPath);
        var reportId = Guid.NewGuid().ToString("N")[..8];
        index[reportId] = new ReportEntry
        {
            Id = reportId,
            Title = request.Title,
            FileName = Path.GetFileName(filePath),
            Format = format,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            FilePath = filePath
        };
        await SaveReportIndex(metadataPath, index);

        await Log(LogLevel.Info, $"Report generated: {filePath}");

        return new
        {
            Success = true,
            ReportId = reportId,
            FilePath = filePath,
            Format = format,
            Message = $"Report '{request.Title}' generated at {filePath}"
        };
    }

    [Display(Name = "list_reports")]
    [Description("List previously generated reports with timestamps and file paths.")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> ListReports(ServiceConfig config, ServiceRequest request)
    {
        var outputDir = config.OutputDirectory ?? Path.Combine(Path.GetTempPath(), "hq-reports");
        var metadataPath = Path.Combine(outputDir, ".report-index.json");
        var index = await LoadReportIndex(metadataPath);

        var reports = index.Values.Select(r => new
        {
            r.Id,
            r.Title,
            r.FileName,
            r.Format,
            r.CreatedAt,
            Exists = File.Exists(r.FilePath)
        }).OrderByDescending(r => r.CreatedAt).ToList();

        return new { Total = reports.Count, Reports = reports };
    }

    [Display(Name = "get_report")]
    [Description("Read the content of a previously generated report by its report ID.")]
    [Parameters("""{"type":"object","properties":{"reportId":{"type":"string","description":"The report ID returned from generate_report or list_reports"}},"required":["reportId"]}""")]
    public async Task<object> GetReport(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReportId))
            throw new ArgumentException("Missing required parameter: reportId");

        var outputDir = config.OutputDirectory ?? Path.Combine(Path.GetTempPath(), "hq-reports");
        var metadataPath = Path.Combine(outputDir, ".report-index.json");
        var index = await LoadReportIndex(metadataPath);

        if (!index.TryGetValue(request.ReportId, out var entry))
            return new { Success = false, Message = $"Report '{request.ReportId}' not found" };

        if (!File.Exists(entry.FilePath))
            return new { Success = false, Message = $"Report file not found at {entry.FilePath}" };

        var content = await File.ReadAllTextAsync(entry.FilePath);

        return new
        {
            Success = true,
            entry.Id,
            entry.Title,
            entry.Format,
            entry.CreatedAt,
            entry.FilePath,
            Content = content
        };
    }

    // ───────────────────────────── Helpers ─────────────────────────────

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return sanitized.Replace(' ', '-').ToLowerInvariant();
    }

    private static string WrapInHtmlTemplate(string title, string body)
    {
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
                <meta charset="UTF-8">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{{title}}</title>
                <style>
                    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; max-width: 800px; margin: 2em auto; padding: 0 1em; line-height: 1.6; color: #333; }
                    h1 { border-bottom: 2px solid #eee; padding-bottom: 0.3em; }
                    h2 { border-bottom: 1px solid #eee; padding-bottom: 0.2em; }
                    table { border-collapse: collapse; width: 100%; margin: 1em 0; }
                    th, td { border: 1px solid #ddd; padding: 8px 12px; text-align: left; }
                    th { background-color: #f5f5f5; }
                    code { background-color: #f5f5f5; padding: 2px 6px; border-radius: 3px; }
                    pre { background-color: #f5f5f5; padding: 1em; border-radius: 5px; overflow-x: auto; }
                </style>
            </head>
            <body>
                <h1>{{title}}</h1>
                {{body}}
                <footer style="margin-top: 3em; padding-top: 1em; border-top: 1px solid #eee; color: #999; font-size: 0.85em;">
                    Generated by HQ Report Generator on {{DateTime.UtcNow:yyyy-MM-dd HH:mm}} UTC
                </footer>
            </body>
            </html>
            """;
    }

    private static async Task<Dictionary<string, ReportEntry>> LoadReportIndex(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, ReportEntry>();

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<Dictionary<string, ReportEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Dictionary<string, ReportEntry>();
    }

    private static async Task SaveReportIndex(string path, Dictionary<string, ReportEntry> index)
    {
        var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private record ReportEntry
    {
        public string Id { get; init; }
        public string Title { get; init; }
        public string FileName { get; init; }
        public string Format { get; init; }
        public string CreatedAt { get; init; }
        public string FilePath { get; init; }
    }
}
