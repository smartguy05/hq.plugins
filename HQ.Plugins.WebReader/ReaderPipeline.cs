using System.Text;
using HQ.Plugins.WebReader.Models;
using HtmlAgilityPack;
using SmartReader;

namespace HQ.Plugins.WebReader;

/// <summary>
/// Pure HTML-&gt;markdown conversion helpers. No browser/network dependency, so they
/// are fully unit-testable against sample HTML.
///
/// Pipeline (matches how Jina Reader / Firecrawl work):
///   1. SmartReader (Mozilla Readability port) extracts the main content, stripping
///      nav/sidebars/footers/ads.
///   2. ReverseMarkdown converts the cleaned HTML to compact, LLM-friendly markdown.
/// </summary>
public static class ReaderPipeline
{
    private static readonly string[] NonContentTags = { "script", "style", "noscript", "svg", "template", "iframe" };

    private static ReverseMarkdown.Converter CreateConverter() => new(new ReverseMarkdown.Config
    {
        GithubFlavored = true,
        UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass,
        RemoveComments = true,
        SmartHrefHandling = true
    });

    public record ReaderResult(
        string Title,
        string Byline,
        string SiteName,
        int Length,
        string Markdown,
        bool Truncated,
        bool UsedFallback);

    public record LinkInfo(string Text, string Href);

    /// <summary>
    /// Extract the main article content and convert it to markdown. Falls back to
    /// converting the whole body when Readability can't find a readable article
    /// (e.g. search results, dashboards).
    /// </summary>
    public static ReaderResult ToMarkdown(string html, string url, string pageTitle, ServiceConfig config)
    {
        var converter = CreateConverter();

        string contentHtml = null;
        string title = pageTitle;
        string byline = null;
        string siteName = null;
        var usedFallback = false;

        try
        {
            var article = new Reader(url ?? "https://localhost/", html).GetArticle();
            if (article is { IsReadable: true } && !string.IsNullOrWhiteSpace(article.Content))
            {
                contentHtml = article.Content;
                if (!string.IsNullOrWhiteSpace(article.Title)) title = article.Title;
                byline = article.Author ?? article.Byline;
                siteName = article.SiteName;
            }
        }
        catch
        {
            // Readability failed entirely — fall through to the body fallback.
        }

        if (string.IsNullOrWhiteSpace(contentHtml))
        {
            usedFallback = true;
            contentHtml = ExtractBodyHtml(html);
        }

        var markdown = converter.Convert(contentHtml ?? string.Empty);
        markdown = CollapseBlankLines(markdown).Trim();

        var fullLength = markdown.Length;
        var max = config.MaxContentLength > 0 ? config.MaxContentLength : int.MaxValue;
        var truncated = markdown.Length > max;
        if (truncated)
            markdown = markdown[..max] + "\n\n[... content truncated ...]";

        if (config.IncludeMetadataHeader)
            markdown = BuildHeader(title, byline, siteName, url) + markdown;

        return new ReaderResult(title, byline, siteName, fullLength, markdown, truncated, usedFallback);
    }

    /// <summary>
    /// Convert the entire (de-noised) page body to markdown — used by search_page so
    /// matches in tables/nav aren't lost to Readability's main-content filter.
    /// </summary>
    public static string FullMarkdown(string html)
    {
        var converter = CreateConverter();
        var markdown = converter.Convert(ExtractBodyHtml(html) ?? string.Empty);
        return CollapseBlankLines(markdown).Trim();
    }

    /// <summary>
    /// All anchors on the page as [text](absolute-href), deduped by href, optionally
    /// filtered by a substring match on text or href.
    /// </summary>
    public static List<LinkInfo> ExtractLinks(string html, string baseUrl, string filter)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html ?? string.Empty);

        var baseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var b) ? b : null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<LinkInfo>();

        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null) return results;

        foreach (var a in anchors)
        {
            var rawHref = HtmlEntity.DeEntitize(a.GetAttributeValue("href", "")).Trim();
            if (string.IsNullOrEmpty(rawHref) || rawHref.StartsWith('#') ||
                rawHref.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
                rawHref.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;

            var href = ResolveHref(baseUri, rawHref);
            var text = HtmlEntity.DeEntitize(a.InnerText ?? "").Trim();
            text = string.Join(' ', text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrEmpty(text)) text = href;

            if (!string.IsNullOrWhiteSpace(filter) &&
                text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                href.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (seen.Add(href))
                results.Add(new LinkInfo(text, href));
        }

        return results;
    }

    /// <summary>
    /// Return only the markdown lines containing the query, with surrounding context.
    /// Modeled on HQ.Plugins.HeadlessBrowser/Pipeline/PageSearcher.cs but operates on
    /// a character window rather than fixed context lines.
    /// </summary>
    public static (int MatchCount, string Snippets) SearchMarkdown(string markdown, string query, int contextChars)
    {
        if (string.IsNullOrWhiteSpace(markdown) || string.IsNullOrWhiteSpace(query))
            return (0, string.Empty);

        if (contextChars <= 0) contextChars = 200;

        var matches = new List<string>();
        var searchFrom = 0;
        while (true)
        {
            var idx = markdown.IndexOf(query, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;

            var start = Math.Max(0, idx - contextChars);
            var end = Math.Min(markdown.Length, idx + query.Length + contextChars);
            var snippet = markdown[start..end].Trim();
            if (start > 0) snippet = "…" + snippet;
            if (end < markdown.Length) snippet += "…";
            matches.Add(snippet);

            searchFrom = idx + query.Length;
        }

        if (matches.Count == 0)
            return (0, string.Empty);

        var sb = new StringBuilder();
        for (var i = 0; i < matches.Count; i++)
        {
            sb.AppendLine($"--- match {i + 1} ---");
            sb.AppendLine(matches[i]);
            sb.AppendLine();
        }

        return (matches.Count, sb.ToString().TrimEnd());
    }

    private static string ResolveHref(Uri baseUri, string rawHref)
    {
        // Resolve against the base first: this both turns relative paths into absolute
        // URLs and leaves already-absolute hrefs intact. (Parsing rawHref as absolute up
        // front would misread a leading-slash path like "/about" as a file:// URI on Unix.)
        if (baseUri is not null && Uri.TryCreate(baseUri, rawHref, out var combined))
            return combined.ToString();
        if (Uri.TryCreate(rawHref, UriKind.Absolute, out var abs))
            return abs.ToString();
        return rawHref;
    }

    private static string ExtractBodyHtml(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html ?? string.Empty);

        foreach (var tag in NonContentTags)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes is null) continue;
            foreach (var n in nodes.ToList())
                n.Remove();
        }

        var body = doc.DocumentNode.SelectSingleNode("//body");
        return (body ?? doc.DocumentNode).InnerHtml;
    }

    private static string BuildHeader(string title, string byline, string siteName, string url)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title)) sb.AppendLine($"# {title.Trim()}");
        if (!string.IsNullOrWhiteSpace(byline)) sb.AppendLine($"*{byline.Trim()}*");
        if (!string.IsNullOrWhiteSpace(siteName)) sb.AppendLine($"Source: {siteName.Trim()}");
        if (!string.IsNullOrWhiteSpace(url)) sb.AppendLine($"URL: {url}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string CollapseBlankLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var blankRun = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                blankRun++;
                if (blankRun <= 1) sb.Append('\n');
            }
            else
            {
                blankRun = 0;
                sb.Append(line.TrimEnd()).Append('\n');
            }
        }
        return sb.ToString();
    }
}
