using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Identity;
using HQ.Plugins.Microsoft365.Models;

namespace HQ.Plugins.Microsoft365.Graph;

/// <summary>
/// Excel workbook operations via the Graph REST workbook API. Uses raw REST rather than the
/// typed SDK because cell-value payloads ({"values":[[...]]}) map directly to JSON, avoiding
/// the SDK's UntypedNode wrapping.
/// </summary>
public class ExcelClient
{
    private const string Base = "https://graph.microsoft.com/v1.0";
    private static readonly HttpClient Http = new();

    private readonly ClientSecretCredential _credential;
    private readonly string _defaultDriveId;

    public ExcelClient(ServiceConfig config)
    {
        _credential = GraphClientFactory.CreateCredential(config);
        _defaultDriveId = config.DefaultDriveId;
    }

    private string DriveId(ServiceRequest r)
    {
        var id = string.IsNullOrWhiteSpace(r.DriveId) ? _defaultDriveId : r.DriveId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException("driveId is required (or set DefaultDriveId in the plugin config).");
        return id;
    }

    private string ItemBase(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.ItemId)) throw new InvalidOperationException("itemId is required for Excel operations.");
        return $"{Base}/drives/{DriveId(r)}/items/{r.ItemId}/workbook";
    }

    private static string WorksheetSegment(ServiceRequest r)
    {
        var ws = r.Worksheet ?? r.WorksheetName;
        return string.IsNullOrWhiteSpace(ws) ? "worksheets" : $"worksheets/{Uri.EscapeDataString(ws)}";
    }

    public async Task<object> ListWorksheets(ServiceRequest r)
    {
        var json = await SendAsync(HttpMethod.Get, $"{ItemBase(r)}/worksheets", null);
        var sheets = json.RootElement.GetProperty("value").EnumerateArray().Select(w => new
        {
            Id = w.GetProperty("id").GetString(),
            Name = w.GetProperty("name").GetString(),
            Position = w.TryGetProperty("position", out var p) ? p.GetInt32() : 0,
            Visibility = w.TryGetProperty("visibility", out var v) ? v.GetString() : null
        });
        return new { Success = true, Worksheets = sheets };
    }

    public async Task<object> GetRange(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Range)) return new { Success = false, Error = "range (A1 address) is required" };
        if (string.IsNullOrWhiteSpace(r.Worksheet) && string.IsNullOrWhiteSpace(r.WorksheetName))
            return new { Success = false, Error = "worksheet is required" };

        var url = $"{ItemBase(r)}/{WorksheetSegment(r)}/range(address='{Uri.EscapeDataString(r.Range)}')";
        var json = await SendAsync(HttpMethod.Get, url, null);
        var values = json.RootElement.GetProperty("values");
        return new { Success = true, Range = r.Range, Values = JsonSerializer.Deserialize<object>(values.GetRawText()) };
    }

    public async Task<object> UpdateRange(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Range)) return new { Success = false, Error = "range (A1 address) is required" };
        if (r.Values is null) return new { Success = false, Error = "values (2D array) is required" };
        if (string.IsNullOrWhiteSpace(r.Worksheet) && string.IsNullOrWhiteSpace(r.WorksheetName))
            return new { Success = false, Error = "worksheet is required" };

        var url = $"{ItemBase(r)}/{WorksheetSegment(r)}/range(address='{Uri.EscapeDataString(r.Range)}')";
        var body = JsonSerializer.Serialize(new { values = r.Values });
        await SendAsync(HttpMethod.Patch, url, body);
        return new { Success = true, Range = r.Range, UpdatedRows = r.Values.Count };
    }

    public async Task<object> AppendRow(ServiceRequest r)
    {
        if (r.Values is null || r.Values.Count == 0) return new { Success = false, Error = "values (2D array) is required" };
        if (string.IsNullOrWhiteSpace(r.Worksheet) && string.IsNullOrWhiteSpace(r.WorksheetName))
            return new { Success = false, Error = "worksheet is required" };

        // Find the first empty row below the used range.
        var usedUrl = $"{ItemBase(r)}/{WorksheetSegment(r)}/usedRange(valuesOnly=true)?$select=address";
        string usedAddress = "";
        try
        {
            var used = await SendAsync(HttpMethod.Get, usedUrl, null);
            usedAddress = used.RootElement.TryGetProperty("address", out var a) ? a.GetString() ?? "" : "";
        }
        catch
        {
            // Empty sheet → usedRange may 404; append at row 1.
        }

        var startRow = A1Helper.NextRowFromUsedRange(usedAddress);
        var cols = r.Values.Max(row => row.Count);
        var target = A1Helper.BuildRangeAddress(startRow, r.Values.Count, cols);

        var url = $"{ItemBase(r)}/{WorksheetSegment(r)}/range(address='{target}')";
        var body = JsonSerializer.Serialize(new { values = r.Values });
        await SendAsync(HttpMethod.Patch, url, body);
        return new { Success = true, Range = target, AppendedRows = r.Values.Count };
    }

    public async Task<object> AddWorksheet(ServiceRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) && string.IsNullOrWhiteSpace(r.WorksheetName))
            return new { Success = false, Error = "name is required" };
        var name = r.Name ?? r.WorksheetName;
        var url = $"{ItemBase(r)}/worksheets/add";
        var json = await SendAsync(HttpMethod.Post, url, JsonSerializer.Serialize(new { name }));
        return new
        {
            Success = true,
            Id = json.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null,
            Name = json.RootElement.TryGetProperty("name", out var n) ? n.GetString() : name
        };
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string url, string jsonBody)
    {
        var token = await GraphClientFactory.GetTokenAsync(_credential);
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (jsonBody is not null)
            req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Graph {(int)resp.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse("{}") : JsonDocument.Parse(text);
    }
}
