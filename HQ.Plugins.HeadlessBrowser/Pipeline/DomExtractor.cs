using System.Text.Json;
using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser.Pipeline;

public static class DomExtractor
{
    // Inline the JS to avoid embedded resource complexity with dynamic loading
    private const string ExtractScript = """
        (maxTextLength) => {
            const SKIP_TAGS = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'SVG', 'LINK', 'META']);
            const INTERACTIVE_TAGS = new Set(['A', 'BUTTON', 'INPUT', 'TEXTAREA', 'SELECT', 'DETAILS', 'SUMMARY']);

            function isHidden(el) {
                if (el.getAttribute('aria-hidden') === 'true') return true;
                const style = window.getComputedStyle(el);
                return style.display === 'none' || style.visibility === 'hidden' || (style.opacity === '0' && !INTERACTIVE_TAGS.has(el.tagName));
            }

            function isTrackingPixel(el) {
                if (el.tagName !== 'IMG') return false;
                const w = el.naturalWidth || parseInt(el.getAttribute('width') || '0');
                const h = el.naturalHeight || parseInt(el.getAttribute('height') || '0');
                return w <= 1 && h <= 1;
            }

            function extractNode(el, depth) {
                if (depth > 30) return null;
                if (el.nodeType === 3) {
                    const text = el.textContent.trim();
                    if (!text) return null;
                    return { type: 'text', content: text.slice(0, maxTextLength) };
                }
                if (el.nodeType !== 1) return null;

                const tag = el.tagName;
                if (SKIP_TAGS.has(tag)) return null;
                if (isHidden(el)) return null;
                if (isTrackingPixel(el)) return null;

                const node = { tag: tag.toLowerCase() };
                if (el.id) node.id = el.id;
                if (el.getAttribute('role')) node.role = el.getAttribute('role');
                if (el.getAttribute('aria-label')) node.ariaLabel = el.getAttribute('aria-label');
                if (el.getAttribute('name')) node.name = el.getAttribute('name');
                if (tag === 'A' && el.href) node.href = el.href;
                if (tag === 'IMG' && el.alt) node.alt = el.alt;
                if (tag === 'IMG' && el.src) node.src = el.src.slice(0, 200);
                if (tag === 'INPUT') {
                    node.inputType = el.type;
                    if (el.placeholder) node.placeholder = el.placeholder;
                    if (el.value) node.value = el.value.slice(0, 50);
                }

                const children = [];
                for (const child of el.childNodes) {
                    const extracted = extractNode(child, depth + 1);
                    if (extracted) children.push(extracted);
                }
                if (children.length > 0) node.children = children;
                return node;
            }

            return extractNode(document.body, 0);
        }
        """;

    public static async Task<DomNode> ExtractAsync(IPage page, int maxTextLength = 100)
    {
        try
        {
            var result = await page.EvaluateAsync<JsonElement>(ExtractScript, maxTextLength);
            return ParseNode(result);
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }

    private static DomNode ParseNode(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Null)
            return null;

        if (el.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text")
        {
            return new DomNode
            {
                IsText = true,
                TextContent = el.GetProperty("content").GetString()
            };
        }

        var node = new DomNode
        {
            Tag = el.TryGetProperty("tag", out var tag) ? tag.GetString() : null,
            Id = el.TryGetProperty("id", out var id) ? id.GetString() : null,
            Role = el.TryGetProperty("role", out var role) ? role.GetString() : null,
            AriaLabel = el.TryGetProperty("ariaLabel", out var al) ? al.GetString() : null,
            Name = el.TryGetProperty("name", out var name) ? name.GetString() : null,
            Href = el.TryGetProperty("href", out var href) ? href.GetString() : null,
            Alt = el.TryGetProperty("alt", out var alt) ? alt.GetString() : null,
            Src = el.TryGetProperty("src", out var src) ? src.GetString() : null,
            InputType = el.TryGetProperty("inputType", out var it) ? it.GetString() : null,
            Placeholder = el.TryGetProperty("placeholder", out var ph) ? ph.GetString() : null,
            Value = el.TryGetProperty("value", out var val) ? val.GetString() : null
        };

        if (el.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                var childNode = ParseNode(child);
                if (childNode != null)
                    node.Children.Add(childNode);
            }
        }

        return node;
    }
}

public class DomNode
{
    public bool IsText { get; set; }
    public string TextContent { get; set; }
    public string Tag { get; set; }
    public string Id { get; set; }
    public string Role { get; set; }
    public string AriaLabel { get; set; }
    public string Name { get; set; }
    public string Href { get; set; }
    public string Alt { get; set; }
    public string Src { get; set; }
    public string InputType { get; set; }
    public string Placeholder { get; set; }
    public string Value { get; set; }
    public List<DomNode> Children { get; set; } = new();

    public bool HasSemanticMeaning => !string.IsNullOrEmpty(Id) || !string.IsNullOrEmpty(Role) || !string.IsNullOrEmpty(AriaLabel);
}
