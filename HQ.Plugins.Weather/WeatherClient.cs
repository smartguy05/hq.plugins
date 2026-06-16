using System.Net.Http.Json;
using System.Text.Json;

namespace HQ.Plugins.Weather;

/// <summary>Thin OpenWeatherMap client (One Call 3.0 + Geocoding), modeled on HQ.Plugins.Calendly's CalendlyClient.</summary>
internal class WeatherClient : IDisposable
{
    public const string OneCallBase = "https://api.openweathermap.org/data/3.0";
    public const string GeoBase = "https://api.openweathermap.org/geo/1.0";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;

    public WeatherClient(string apiKey)
    {
        _apiKey = apiKey;
        _httpClient = new HttpClient();
    }

    /// <summary>Resolve a place name to coordinates. Returns null if nothing matched.</summary>
    public async Task<(double Lat, double Lon, string Label)?> GeocodeAsync(string query)
    {
        var url = $"{GeoBase}/direct?q={Uri.EscapeDataString(query)}&limit=1&appid={_apiKey}";
        var arr = await GetAsync(url);
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
        var first = arr[0];
        var lat = first.GetProperty("lat").GetDouble();
        var lon = first.GetProperty("lon").GetDouble();
        var name = first.TryGetProperty("name", out var n) ? n.GetString() : query;
        var country = first.TryGetProperty("country", out var c) ? c.GetString() : null;
        var state = first.TryGetProperty("state", out var s) ? s.GetString() : null;
        var label = string.Join(", ", new[] { name, state, country }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return (lat, lon, label);
    }

    public Task<JsonElement> OneCallAsync(double lat, double lon, string units, string exclude) =>
        GetAsync($"{OneCallBase}/onecall?lat={lat}&lon={lon}&units={units}&exclude={exclude}&appid={_apiKey}");

    public async Task<JsonElement> GetAsync(string url)
    {
        var response = await _httpClient.GetAsync(url);
        await EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        if (body.Length > 500) body = body[..500] + "…";
        throw new HttpRequestException($"OpenWeatherMap API error {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
