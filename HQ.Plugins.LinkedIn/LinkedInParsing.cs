using System.Text.Json;

namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Best-effort, never-throwing extractors that distil LinkedIn's verbose normalized Voyager
/// payloads into small summaries. They are deliberately tolerant of missing fields and shape
/// drift: callers always receive the raw JSON alongside the summary, so a parsing miss
/// degrades to "raw only" rather than an error. Pure functions — unit-tested against fixtures.
/// </summary>
public static class LinkedInParsing
{
    /// <summary>First present string-valued property among <paramref name="names"/>, else null.</summary>
    public static string Str(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();
        return null;
    }

    /// <summary>Summarizes a legacy <c>profileView</c> payload (its nested <c>profile</c> object).</summary>
    public static Dictionary<string, string> SummarizeProfile(JsonElement profileView)
    {
        var result = new Dictionary<string, string>();
        if (profileView.ValueKind != JsonValueKind.Object) return result;

        var profile = profileView.TryGetProperty("profile", out var p) && p.ValueKind == JsonValueKind.Object
            ? p
            : profileView;

        Add(result, "firstName", Str(profile, "firstName"));
        Add(result, "lastName", Str(profile, "lastName"));
        Add(result, "headline", Str(profile, "headline"));
        Add(result, "summary", Str(profile, "summary"));
        Add(result, "location", Str(profile, "locationName", "geoLocationName"));
        Add(result, "industry", Str(profile, "industryName"));
        Add(result, "publicIdentifier", Str(profile, "publicIdentifier"));
        return result;
    }

    /// <summary>Summarizes a company payload (universalName query → <c>elements[0]</c> or root).</summary>
    public static Dictionary<string, string> SummarizeCompany(JsonElement company)
    {
        var root = FirstElement(company) ?? company;
        var result = new Dictionary<string, string>();
        if (root.ValueKind != JsonValueKind.Object) return result;

        Add(result, "name", Str(root, "name", "universalName"));
        Add(result, "universalName", Str(root, "universalName"));
        Add(result, "description", Str(root, "description", "tagline"));
        Add(result, "industry", Str(root, "industry"));
        if (root.TryGetProperty("staffCount", out var sc) && sc.ValueKind == JsonValueKind.Number)
            result["staffCount"] = sc.GetRawText();
        return result;
    }

    /// <summary>
    /// Summarizes typeahead/search hits into a flat list of {title, subtitle, urn}. Reads the
    /// <c>elements</c> array; each element's title/subtitle may be a plain string or an object
    /// with a <c>text</c> field.
    /// </summary>
    public static List<Dictionary<string, string>> SummarizeHits(JsonElement payload)
    {
        var hits = new List<Dictionary<string, string>>();
        if (payload.ValueKind != JsonValueKind.Object) return hits;
        if (!payload.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            return hits;

        foreach (var el in elements.EnumerateArray())
        {
            var hit = new Dictionary<string, string>();
            Add(hit, "title", Text(el, "title"));
            Add(hit, "subtitle", Text(el, "subtext", "subtitle", "headline"));
            Add(hit, "urn", Str(el, "targetUrn", "objectUrn", "entityUrn"));
            if (hit.Count > 0) hits.Add(hit);
        }
        return hits;
    }

    // ---- helpers ----

    private static void Add(Dictionary<string, string> d, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) d[key] = value;
    }

    /// <summary>A field that's either a string or an object carrying a <c>text</c> string.</summary>
    private static string Text(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            if (!el.TryGetProperty(name, out var p)) continue;
            if (p.ValueKind == JsonValueKind.String) return p.GetString();
            if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                return t.GetString();
        }
        return null;
    }

    private static JsonElement? FirstElement(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object &&
            el.TryGetProperty("elements", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray()) return item;
        }
        return null;
    }
}
