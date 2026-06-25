using System.Text.Json;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using HQ.Plugins.GoogleWorkspace.Models;

namespace HQ.Plugins.GoogleWorkspace.Clients;

/// <summary>Google Sheets operations (spreadsheet surface).</summary>
public class SheetsClient
{
    private readonly SheetsService _sheets;

    public SheetsClient(ServiceConfig config) => _sheets = GoogleClientFactory.CreateSheets(config);

    public async Task<object> Create(SheetsCreateArgs r)
    {
        var spreadsheet = new Spreadsheet
        {
            Properties = new SpreadsheetProperties { Title = r.Title ?? r.Name ?? "Untitled spreadsheet" }
        };
        var created = await _sheets.Spreadsheets.Create(spreadsheet).ExecuteAsync();
        return new
        {
            Success = true,
            SpreadsheetId = created.SpreadsheetId,
            created.Properties.Title,
            WebViewLink = created.SpreadsheetUrl
        };
    }

    public async Task<object> GetValues(SheetsGetValuesArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (spreadsheetId) is required" };
        if (string.IsNullOrWhiteSpace(r.Range)) return new { Success = false, Error = "range (A1 notation) is required" };

        var response = await _sheets.Spreadsheets.Values.Get(r.FileId, r.Range).ExecuteAsync();
        return new { Success = true, response.Range, Values = response.Values ?? [] };
    }

    public async Task<object> UpdateValues(SheetsUpdateValuesArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (spreadsheetId) is required" };
        if (string.IsNullOrWhiteSpace(r.Range)) return new { Success = false, Error = "range (A1 notation) is required" };
        if (r.Values is null) return new { Success = false, Error = "values (2D array) is required" };

        var body = new ValueRange { Values = NormalizeValues(r.Values) };
        var update = _sheets.Spreadsheets.Values.Update(body, r.FileId, r.Range);
        update.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
        var result = await update.ExecuteAsync();
        return new { Success = true, result.UpdatedRange, result.UpdatedRows, result.UpdatedCells };
    }

    public async Task<object> AppendRow(SheetsAppendRowArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (spreadsheetId) is required" };
        if (r.Values is null) return new { Success = false, Error = "values (2D array) is required" };

        var range = string.IsNullOrWhiteSpace(r.Range) ? "A1" : r.Range;
        var body = new ValueRange { Values = NormalizeValues(r.Values) };
        var append = _sheets.Spreadsheets.Values.Append(body, r.FileId, range);
        append.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
        append.InsertDataOption = SpreadsheetsResource.ValuesResource.AppendRequest.InsertDataOptionEnum.INSERTROWS;
        var result = await append.ExecuteAsync();
        return new { Success = true, UpdatedRange = result.Updates?.UpdatedRange, UpdatedRows = result.Updates?.UpdatedRows };
    }

    public async Task<object> ClearValues(SheetsClearValuesArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (spreadsheetId) is required" };
        if (string.IsNullOrWhiteSpace(r.Range)) return new { Success = false, Error = "range (A1 notation) is required" };

        var result = await _sheets.Spreadsheets.Values.Clear(new ClearValuesRequest(), r.FileId, r.Range).ExecuteAsync();
        return new { Success = true, result.ClearedRange };
    }

    public async Task<object> ListSheets(SheetsListSheetsArgs r)
    {
        if (string.IsNullOrWhiteSpace(r.FileId)) return new { Success = false, Error = "fileId (spreadsheetId) is required" };
        var ss = await _sheets.Spreadsheets.Get(r.FileId).ExecuteAsync();
        var sheets = ss.Sheets?.Select(s => new
        {
            SheetId = s.Properties.SheetId,
            Title = s.Properties.Title,
            Index = s.Properties.Index,
            Rows = s.Properties.GridProperties?.RowCount,
            Columns = s.Properties.GridProperties?.ColumnCount
        });
        return new { Success = true, ss.Properties.Title, Sheets = sheets ?? [] };
    }

    /// <summary>
    /// Converts the LLM-supplied 2D JsonElement grid into the object grid the Sheets API
    /// expects (string / double / bool / null). Public and pure for unit testing.
    /// </summary>
    public static IList<IList<object>> NormalizeValues(List<List<JsonElement>> rows)
    {
        var result = new List<IList<object>>();
        if (rows is null) return result;
        foreach (var row in rows)
        {
            var cells = new List<object>();
            foreach (var cell in row) cells.Add(NormalizeCell(cell));
            result.Add(cells);
        }
        return result;
    }

    private static object NormalizeCell(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => cell.GetString(),
        JsonValueKind.Number => cell.TryGetInt64(out var l) ? (object)l : cell.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => cell.GetRawText()
    };
}
