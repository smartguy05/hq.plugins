using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.HeadlessBrowser.Models;
using HQ.Plugins.HeadlessBrowser.Pipeline;
using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser;

public class HeadlessBrowserService
{
    private readonly IBrowserClient _client;
    private readonly ServiceConfig _config;
    private readonly LogDelegate _logger;

    public HeadlessBrowserService(IBrowserClient client, ServiceConfig config, LogDelegate logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    [Display(Name = BrowserMethods.NavigateToUrl)]
    [Description("Navigate to a URL and return the page title and a content summary. This initializes the browser session if not already open.")]
    [Parameters("""{"type":"object","properties":{"url":{"type":"string","description":"The URL to navigate to"}},"required":["url"]}""")]
    public async Task<object> NavigateToUrl(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new ArgumentException("Missing required parameter: url");

        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                IResponse response;
                try
                {
                    response = await page.GotoAsync(request.Url, new PageGotoOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = config.DefaultTimeoutMs
                    });

                    // Best-effort wait for network to settle, but don't fail if it times out
                    try
                    {
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                            new PageWaitForLoadStateOptions { Timeout = 10000 });
                    }
                    catch (TimeoutException) { }
                }
                catch (TimeoutException)
                {
                    // Page took too long even for DOMContentLoaded — return what we have
                    return (object)new
                    {
                        Success = false,
                        Url = request.Url,
                        Message = $"Navigation timed out after {config.DefaultTimeoutMs}ms. The page may be slow or unresponsive."
                    };
                }

                var title = await page.TitleAsync();
                var url = page.Url;

                string summary;
                string format = "text";

                if (config.PreferAriaSnapshot)
                {
                    var yaml = await AriaSnapshotExtractor.ExtractAsync(page, config.DefaultTimeoutMs);
                    if (AriaSnapshotExtractor.IsUsable(yaml))
                    {
                        // Build ref-annotated snapshot
                        var snapshot = RefAssigner.Assign(yaml, url);
                        summary = AriaSnapshotExtractor.Truncate(snapshot.AnnotatedYaml, Math.Min(config.MaxSnapshotLines, 200));
                        format = "aria";
                    }
                    else
                    {
                        var textContent = await page.EvaluateAsync<string>(
                            "() => document.body?.innerText || ''");
                        summary = textContent.Length > 2000
                            ? textContent[..2000] + "..."
                            : textContent;
                    }
                }
                else
                {
                    var textContent = await page.EvaluateAsync<string>(
                        "() => document.body?.innerText || ''");
                    summary = textContent.Length > 2000
                        ? textContent[..2000] + "..."
                        : textContent;
                }

                return (object)new
                {
                    Success = true,
                    Title = title,
                    Url = url,
                    StatusCode = response?.Status,
                    ContentSummary = summary.Trim(),
                    Format = format
                };
            });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
        {
            return new
            {
                Success = false,
                Message = "Playwright browsers are not installed. Run 'pwsh bin/Debug/net9.0/Plugins/playwright.ps1 install chromium' to install."
            };
        }
        catch (PlaywrightException ex)
        {
            return new
            {
                Success = false,
                Url = request.Url,
                Message = $"Navigation failed: {ex.Message}"
            };
        }
    }

    [Display(Name = BrowserMethods.GetPageContent)]
    [Description("Get page content in aria (default, compact accessibility tree), compressed (clean DOM with noise removal), text, or html format. The 'aria' format uses ~80% fewer tokens.")]
    [Parameters("""{"type":"object","properties":{"selector":{"type":"string","description":"CSS selector for a specific element (optional)"},"format":{"type":"string","description":"'aria' (default), 'compressed' (clean DOM), 'text', or 'html'"},"taskHint":{"type":"string","description":"Filter hint: 'form_fill', 'navigation', 'data_extraction', 'search', or 'general' (default)"},"maxLength":{"type":"integer","description":"Maximum content length (default 50000 for text/html, 300 lines for aria)"}},"required":[]}""")]
    public async Task<object> GetPageContent(ServiceConfig config, ServiceRequest request)
    {
        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var format = request.Format?.ToLowerInvariant()
                             ?? request.ContentType?.ToLowerInvariant()
                             ?? (config.PreferAriaSnapshot ? "aria" : "text");

                if (format == "aria")
                {
                    string yaml;
                    if (!string.IsNullOrWhiteSpace(request.Selector))
                    {
                        try
                        {
                            yaml = await page.Locator(request.Selector).AriaSnapshotAsync(
                                new LocatorAriaSnapshotOptions { Timeout = config.DefaultTimeoutMs });
                        }
                        catch (PlaywrightException)
                        {
                            return (object)new { Success = false, Message = $"Element not found: {request.Selector}" };
                        }
                    }
                    else
                    {
                        yaml = await AriaSnapshotExtractor.ExtractAsync(page, config.DefaultTimeoutMs);
                    }

                    if (!AriaSnapshotExtractor.IsUsable(yaml))
                    {
                        // Fall back to text
                        var fallbackText = await page.EvaluateAsync<string>("() => document.body?.innerText || ''");
                        var maxLen = request.MaxLength ?? 50000;
                        if (fallbackText.Length > maxLen)
                            fallbackText = fallbackText[..maxLen] + "...(truncated)";

                        return (object)new
                        {
                            Success = true,
                            Url = page.Url,
                            Format = "text",
                            FallbackReason = "AriaSnapshot too sparse for this page",
                            Length = fallbackText.Length,
                            Content = fallbackText.Trim()
                        };
                    }

                    // Apply task-scoped filter before ref assignment
                    if (!string.IsNullOrWhiteSpace(request.TaskHint))
                        yaml = TaskFilter.Filter(yaml, request.TaskHint);

                    var maxLines = request.MaxLength ?? config.MaxSnapshotLines;
                    var snapshot = RefAssigner.Assign(yaml, page.Url);
                    var truncated = AriaSnapshotExtractor.Truncate(snapshot.AnnotatedYaml, maxLines);

                    return (object)new
                    {
                        Success = true,
                        Url = page.Url,
                        Format = "aria",
                        Lines = truncated.Split('\n').Length,
                        Content = truncated
                    };
                }

                if (format == "compressed")
                {
                    var domTree = await DomExtractor.ExtractAsync(page, config.TextTruncationLimit);
                    if (domTree == null)
                        return (object)new { Success = false, Message = "Failed to extract DOM" };

                    domTree = DomCompressor.Compress(domTree);
                    if (config.EnableListFolding)
                        domTree = ListFolder.Fold(domTree, config.SimHashSimilarityThreshold);

                    var compressed = DomCompressor.Serialize(domTree);
                    var compMaxLength = request.MaxLength ?? 50000;
                    if (compressed.Length > compMaxLength)
                        compressed = compressed[..compMaxLength] + "\n...(truncated)";

                    return (object)new
                    {
                        Success = true,
                        Url = page.Url,
                        Format = "compressed",
                        Length = compressed.Length,
                        Content = compressed
                    };
                }

                // Existing text/html paths
                var maxLength = request.MaxLength ?? 50000;
                var contentType = format;
                string content;

                if (!string.IsNullOrWhiteSpace(request.Selector))
                {
                    var element = await page.QuerySelectorAsync(request.Selector);
                    if (element is null)
                        return (object)new { Success = false, Message = $"Element not found: {request.Selector}" };

                    content = contentType == "html"
                        ? await element.InnerHTMLAsync()
                        : await element.InnerTextAsync();
                }
                else
                {
                    content = contentType == "html"
                        ? await page.ContentAsync()
                        : await page.EvaluateAsync<string>("() => document.body?.innerText || ''");
                }

                if (content.Length > maxLength)
                    content = content[..maxLength] + "...(truncated)";

                return (object)new
                {
                    Success = true,
                    Url = page.Url,
                    Format = contentType,
                    Length = content.Length,
                    Content = content.Trim()
                };
            });
        }
        catch (PlaywrightException ex)
        {
            return new
            {
                Success = false,
                Message = $"Failed to get page content: {ex.Message}"
            };
        }
    }

    [Display(Name = BrowserMethods.GetInteractiveElements)]
    [Description("List interactive elements (links, buttons, inputs) on the current page. Returns role and name from the accessibility tree. Use format 'legacy' for CSS selectors instead.")]
    [Parameters("""{"type":"object","properties":{"elementType":{"type":"string","description":"Filter: 'links', 'buttons', 'inputs', 'all' (default 'all')"},"selector":{"type":"string","description":"Scope search within this CSS selector"},"format":{"type":"string","description":"'aria' (default, from accessibility tree) or 'legacy' (CSS selectors via DOM walk)"}},"required":[]}""")]
    public async Task<object> GetInteractiveElements(ServiceConfig config, ServiceRequest request)
    {
        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var format = request.Format?.ToLowerInvariant() ?? (config.PreferAriaSnapshot ? "aria" : "legacy");

                if (format == "aria")
                {
                    string yaml;
                    if (!string.IsNullOrWhiteSpace(request.Selector))
                    {
                        try
                        {
                            yaml = await page.Locator(request.Selector).AriaSnapshotAsync(
                                new LocatorAriaSnapshotOptions { Timeout = config.DefaultTimeoutMs });
                        }
                        catch (PlaywrightException)
                        {
                            return (object)new { Success = false, Message = $"Scope element not found: {request.Selector}" };
                        }
                    }
                    else
                    {
                        yaml = await AriaSnapshotExtractor.ExtractAsync(page, config.DefaultTimeoutMs);
                    }

                    if (!AriaSnapshotExtractor.IsUsable(yaml))
                        return await GetInteractiveElementsLegacy(page, request);

                    var allElements = AriaSnapshotExtractor.ParseInteractiveElements(yaml);
                    var elementType = request.ElementType?.ToLowerInvariant() ?? "all";

                    var filtered = elementType switch
                    {
                        "links" => allElements.Where(e => e.Role == "link").ToList(),
                        "buttons" => allElements.Where(e => e.Role is "button" or "menuitem").ToList(),
                        "inputs" => allElements.Where(e => e.Role is "textbox" or "combobox" or "checkbox"
                            or "radio" or "slider" or "spinbutton" or "searchbox" or "switch").ToList(),
                        _ => allElements
                    };

                    return (object)new
                    {
                        Success = true,
                        Url = page.Url,
                        Format = "aria",
                        Count = filtered.Count,
                        Elements = filtered.Select(e => new { e.Role, e.Name }).ToArray()
                    };
                }

                return await GetInteractiveElementsLegacy(page, request);
            });
        }
        catch (PlaywrightException ex)
        {
            return new
            {
                Success = false,
                Message = $"Failed to get interactive elements: {ex.Message}"
            };
        }
    }

    private async Task<object> GetInteractiveElementsLegacy(IPage page, ServiceRequest request)
    {
        var elementType = request.ElementType?.ToLowerInvariant() ?? "all";
        var scope = string.IsNullOrWhiteSpace(request.Selector) ? "document" : $"document.querySelector('{request.Selector.Replace("'", "\\'")}')";

        var js = $$"""
            (scopeSelector) => {
                const scope = scopeSelector === 'document' ? document : document.querySelector(scopeSelector);
                if (!scope) return { error: 'Scope element not found' };
                const results = [];
                const types = '{{elementType}}';

                function getSelector(el) {
                    if (el.id) return '#' + el.id;
                    if (el.name) return el.tagName.toLowerCase() + '[name="' + el.name + '"]';
                    const classes = Array.from(el.classList).slice(0, 2).join('.');
                    if (classes) return el.tagName.toLowerCase() + '.' + classes;
                    return el.tagName.toLowerCase();
                }

                if (types === 'all' || types === 'links') {
                    scope.querySelectorAll('a[href]').forEach((el, i) => {
                        if (i < 50) results.push({ type: 'link', selector: getSelector(el), text: (el.innerText || '').slice(0, 100), href: el.href });
                    });
                }
                if (types === 'all' || types === 'buttons') {
                    scope.querySelectorAll('button, input[type="button"], input[type="submit"], [role="button"]').forEach((el, i) => {
                        if (i < 50) results.push({ type: 'button', selector: getSelector(el), text: (el.innerText || el.value || '').slice(0, 100) });
                    });
                }
                if (types === 'all' || types === 'inputs') {
                    scope.querySelectorAll('input:not([type="button"]):not([type="submit"]):not([type="hidden"]), textarea, select').forEach((el, i) => {
                        if (i < 50) results.push({ type: el.tagName.toLowerCase(), selector: getSelector(el), name: el.name, inputType: el.type, placeholder: el.placeholder, value: (el.value || '').slice(0, 50) });
                    });
                }
                return results;
            }
            """;

        var elements = await page.EvaluateAsync<object>(js,
            string.IsNullOrWhiteSpace(request.Selector) ? "document" : request.Selector);

        return (object)new
        {
            Success = true,
            Url = page.Url,
            Format = "legacy",
            Elements = elements
        };
    }

    [Display(Name = BrowserMethods.GetOutline)]
    [Description("Get a compact structural outline of the current page: headings, navigation, forms, and key interactive elements with ref IDs. Much smaller than full content.")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> GetOutline(ServiceConfig config, ServiceRequest request)
    {
        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var yaml = await AriaSnapshotExtractor.ExtractAsync(page, config.DefaultTimeoutMs);
                if (!AriaSnapshotExtractor.IsUsable(yaml))
                {
                    return (object)new
                    {
                        Success = true,
                        Url = page.Url,
                        Title = await page.TitleAsync(),
                        Outline = "Page has minimal accessibility structure. Use get_page_content with format 'text' instead."
                    };
                }

                var snapshot = RefAssigner.Assign(yaml, page.Url);
                var outline = OutlineBuilder.Build(snapshot.AnnotatedYaml);

                return (object)new
                {
                    Success = true,
                    Url = page.Url,
                    Title = await page.TitleAsync(),
                    Lines = outline.Split('\n').Length,
                    Outline = outline
                };
            });
        }
        catch (PlaywrightException ex)
        {
            return new { Success = false, Message = $"Failed to get outline: {ex.Message}" };
        }
    }

    [Display(Name = BrowserMethods.SearchPage)]
    [Description("Search the current page for text. Returns matching lines with surrounding context and nearby ref IDs. Does not re-fetch the page.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Text to search for (case-insensitive)"}},"required":["query"]}""")]
    public async Task<object> SearchPage(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Missing required parameter: query");

        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var yaml = await AriaSnapshotExtractor.ExtractAsync(page, config.DefaultTimeoutMs);
                if (!AriaSnapshotExtractor.IsUsable(yaml))
                {
                    // Fall back to text search
                    var text = await page.EvaluateAsync<string>("() => document.body?.innerText || ''");
                    var hasMatch = text.Contains(request.Query, StringComparison.OrdinalIgnoreCase);
                    return (object)new
                    {
                        Success = true,
                        Url = page.Url,
                        Format = "text",
                        Found = hasMatch,
                        Message = hasMatch
                            ? $"Text found on page (use get_page_content for full context)"
                            : $"No matches found for: {request.Query}"
                    };
                }

                var snapshot = RefAssigner.Assign(yaml, page.Url);
                var result = PageSearcher.Search(snapshot.AnnotatedYaml, request.Query);

                return (object)new
                {
                    Success = true,
                    Url = page.Url,
                    Format = "aria",
                    Results = result
                };
            });
        }
        catch (PlaywrightException ex)
        {
            return new { Success = false, Message = $"Search failed: {ex.Message}" };
        }
    }

    [Display(Name = BrowserMethods.GetElement)]
    [Description("Get the full subtree of a specific element by ref ID. Use after get_outline to drill into a section.")]
    [Parameters("""{"type":"object","properties":{"ref":{"type":"string","description":"Ref ID to expand (e.g. 'e5')"}},"required":["ref"]}""")]
    public async Task<object> GetElement(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Ref))
            throw new ArgumentException("Missing required parameter: ref");

        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var yaml = await AriaSnapshotExtractor.ExtractAsync(page, config.DefaultTimeoutMs);
                if (!AriaSnapshotExtractor.IsUsable(yaml))
                    return (object)new { Success = false, Message = "AriaSnapshot not available. Use get_page_content instead." };

                var snapshot = RefAssigner.Assign(yaml, page.Url);
                if (!snapshot.RefMap.TryGetValue(request.Ref, out var elementRef))
                    return (object)new { Success = false, StaleRef = true, Message = $"Ref '{request.Ref}' not found. Call get_outline to refresh." };

                // Extract the subtree: from the ref's line to the next line at same or lesser indent
                var lines = snapshot.AnnotatedYaml.Split('\n');
                var startLine = elementRef.LineIndex;
                var startIndent = elementRef.IndentLevel;
                var endLine = startLine + 1;

                while (endLine < lines.Length)
                {
                    var trimmed = lines[endLine].TrimStart();
                    if (trimmed.StartsWith("- "))
                    {
                        var indent = (lines[endLine].Length - trimmed.Length) / 2;
                        if (indent <= startIndent)
                            break;
                    }
                    endLine++;
                }

                var subtree = string.Join('\n', lines[startLine..endLine]);

                return (object)new
                {
                    Success = true,
                    Url = page.Url,
                    Ref = request.Ref,
                    Role = elementRef.Role,
                    Name = elementRef.Name,
                    Lines = endLine - startLine,
                    Content = subtree
                };
            });
        }
        catch (PlaywrightException ex)
        {
            return new { Success = false, Message = $"Failed to get element: {ex.Message}" };
        }
    }

    [Display(Name = BrowserMethods.GetVisibleText)]
    [Description("Get the main readable text content (like reader view). Strips navigation, sidebars, footers, and ads. Good for content-heavy pages.")]
    [Parameters("""{"type":"object","properties":{"maxLength":{"type":"integer","description":"Maximum text length (default 10000)"}},"required":[]}""")]
    public async Task<object> GetVisibleText(ServiceConfig config, ServiceRequest request)
    {
        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var maxLength = request.MaxLength ?? 10000;
                string text;

                // Try main content selectors first
                var mainLocator = page.Locator("main, article, [role='main']").First;
                try
                {
                    text = await mainLocator.InnerTextAsync(new LocatorInnerTextOptions { Timeout = 3000 });
                }
                catch
                {
                    // Fallback to body text
                    text = await page.EvaluateAsync<string>("() => document.body?.innerText || ''");
                }

                if (string.IsNullOrWhiteSpace(text))
                    text = await page.EvaluateAsync<string>("() => document.body?.innerText || ''");

                if (text.Length > maxLength)
                    text = text[..maxLength] + "...(truncated)";

                return (object)new
                {
                    Success = true,
                    Url = page.Url,
                    Length = text.Length,
                    Content = text.Trim()
                };
            });
        }
        catch (PlaywrightException ex)
        {
            return new { Success = false, Message = $"Failed to get visible text: {ex.Message}" };
        }
    }

    [Display(Name = BrowserMethods.ClickElement)]
    [Description("Click an element by ref ID (e.g. 'e5') or CSS selector. Set diffMode to true to get a delta of what changed.")]
    [Parameters("""{"type":"object","properties":{"ref":{"type":"string","description":"Ref ID from page snapshot (e.g. 'e5'). Preferred over selector."},"selector":{"type":"string","description":"CSS selector (fallback if ref not available)"},"diffMode":{"type":"boolean","description":"Return only what changed after the click (default false)"}},"required":[]}""")]
    public async Task<object> ClickElement(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Ref) && string.IsNullOrWhiteSpace(request.Selector))
            throw new ArgumentException("Provide either 'ref' or 'selector'");

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Ref))
            {
                var locator = _client.ResolveRef(request.Ref);
                if (locator is null)
                    return new { Success = false, StaleRef = true, Message = $"Ref '{request.Ref}' not found. Call get_page_content or navigate_to_url to refresh." };

                return await WithDiffIfEnabled(request, async () =>
                {
                    return await _client.ExecuteAsync(async page =>
                    {
                        await locator.ClickAsync();
                        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 }); }
                        catch (TimeoutException) { }

                        return (true, page.Url, await page.TitleAsync(), $"Clicked element: {request.Ref}");
                    });
                });
            }

            return await WithDiffIfEnabled(request, async () =>
            {
                return await _client.ExecuteAsync(async page =>
                {
                    await page.ClickAsync(request.Selector);
                    try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 }); }
                    catch (TimeoutException) { }

                    return (true, page.Url, await page.TitleAsync(), $"Clicked element: {request.Selector}");
                });
            });
        }
        catch (PlaywrightException ex)
        {
            return new
            {
                Success = false,
                Message = $"Click failed: {ex.Message}"
            };
        }
    }

    [Display(Name = BrowserMethods.FillField)]
    [Description("Fill a form field by ref ID (e.g. 'e3') or CSS selector. Clears the field first.")]
    [Parameters("""{"type":"object","properties":{"ref":{"type":"string","description":"Ref ID from page snapshot (e.g. 'e3'). Preferred over selector."},"selector":{"type":"string","description":"CSS selector (fallback if ref not available)"},"value":{"type":"string","description":"The value to type into the field"}},"required":["value"]}""")]
    public async Task<object> FillField(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Ref) && string.IsNullOrWhiteSpace(request.Selector))
            throw new ArgumentException("Provide either 'ref' or 'selector'");
        if (request.Value is null)
            throw new ArgumentException("Missing required parameter: value");

        if (!string.IsNullOrWhiteSpace(request.Ref))
        {
            var locator = _client.ResolveRef(request.Ref);
            if (locator is null)
                return new { Success = false, StaleRef = true, Message = $"Ref '{request.Ref}' not found. Call get_page_content or navigate_to_url to refresh." };

            return await _client.ExecuteAsync(async page =>
            {
                await locator.FillAsync(request.Value);
                return (object)new { Success = true, Message = $"Filled '{request.Ref}' with value" };
            });
        }

        return await _client.ExecuteAsync(async page =>
        {
            await page.FillAsync(request.Selector, request.Value);
            return (object)new { Success = true, Message = $"Filled '{request.Selector}' with value" };
        });
    }

    [Display(Name = BrowserMethods.SubmitForm)]
    [Description("Submit a form by clicking a submit button (by ref or selector). Set diffMode to true to get a delta of what changed.")]
    [Parameters("""{"type":"object","properties":{"ref":{"type":"string","description":"Ref ID of the submit button (e.g. 'e7'). Preferred over selector."},"selector":{"type":"string","description":"CSS selector of the submit button or form element"},"diffMode":{"type":"boolean","description":"Return only what changed after submit (default false)"}},"required":[]}""")]
    public async Task<object> SubmitForm(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Ref) && string.IsNullOrWhiteSpace(request.Selector))
            throw new ArgumentException("Provide either 'ref' or 'selector'");

        try
        {
            if (!string.IsNullOrWhiteSpace(request.Ref))
            {
                var locator = _client.ResolveRef(request.Ref);
                if (locator is null)
                    return new { Success = false, StaleRef = true, Message = $"Ref '{request.Ref}' not found. Call get_page_content or navigate_to_url to refresh." };

                return await WithDiffIfEnabled(request, async () =>
                {
                    return await _client.ExecuteAsync(async page =>
                    {
                        await locator.ClickAsync();
                        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 }); }
                        catch (TimeoutException) { }

                        return (true, page.Url, await page.TitleAsync(), $"Form submitted via: {request.Ref}");
                    });
                });
            }

            return await WithDiffIfEnabled(request, async () =>
            {
                return await _client.ExecuteAsync(async page =>
                {
                    var tagName = await page.EvaluateAsync<string>(
                        "(sel) => document.querySelector(sel)?.tagName?.toLowerCase()", request.Selector);

                    if (tagName == "form")
                        await page.EvaluateAsync("(sel) => document.querySelector(sel).submit()", request.Selector);
                    else
                        await page.ClickAsync(request.Selector);

                    try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10000 }); }
                    catch (TimeoutException) { }

                    return (true, page.Url, await page.TitleAsync(), $"Form submitted via: {request.Selector}");
                });
            });
        }
        catch (PlaywrightException ex)
        {
            return new
            {
                Success = false,
                Message = $"Form submission failed: {ex.Message}"
            };
        }
    }

    [Display(Name = BrowserMethods.TakeScreenshot)]
    [Description("Take a screenshot of the current page and save to disk. Returns the file path to the saved screenshot.")]
    [Parameters("""{"type":"object","properties":{"fileName":{"type":"string","description":"Custom file name for the screenshot (optional, defaults to timestamp)"},"fullPage":{"type":"boolean","description":"Capture the full scrollable page (default false)"},"selector":{"type":"string","description":"CSS selector to screenshot a specific element instead of the full page"}},"required":[]}""")]
    public async Task<object> TakeScreenshot(ServiceConfig config, ServiceRequest request)
    {
        return await _client.ExecuteAsync(async page =>
        {
            var dir = !string.IsNullOrWhiteSpace(config.ScreenshotDirectory)
                ? config.ScreenshotDirectory
                : Path.GetTempPath();

            Directory.CreateDirectory(dir);

            var fileName = !string.IsNullOrWhiteSpace(request.FileName)
                ? request.FileName
                : $"screenshot_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png";

            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            var filePath = Path.Combine(dir, fileName);

            if (!string.IsNullOrWhiteSpace(request.Selector))
            {
                var element = await page.QuerySelectorAsync(request.Selector);
                if (element is null)
                    return (object)new { Success = false, Message = $"Element not found: {request.Selector}" };

                await element.ScreenshotAsync(new ElementHandleScreenshotOptions { Path = filePath });
            }
            else
            {
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = filePath,
                    FullPage = request.FullPage ?? false
                });
            }

            return (object)new
            {
                Success = true,
                FilePath = filePath,
                Message = $"Screenshot saved to {filePath}"
            };
        });
    }

    [Display(Name = BrowserMethods.ExecuteJavascript)]
    [Description("Execute arbitrary JavaScript in the browser page context and return the result. Use for advanced interactions or data extraction.")]
    [Parameters("""{"type":"object","properties":{"script":{"type":"string","description":"JavaScript code to execute in the page context. Use 'return' for expressions or write a function body."}},"required":["script"]}""")]
    public async Task<object> ExecuteJavascript(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Script))
            throw new ArgumentException("Missing required parameter: script");

        try
        {
            return await _client.ExecuteAsync(async page =>
            {
                var result = await page.EvaluateAsync<object>($"() => {{ {request.Script} }}");

                return (object)new
                {
                    Success = true,
                    Result = result
                };
            });
        }
        catch (PlaywrightException ex)
        {
            return new
            {
                Success = false,
                Message = $"JavaScript execution failed: {ex.Message}"
            };
        }
    }

    [Display(Name = BrowserMethods.CloseBrowser)]
    [Description("Close the browser session and release all resources. Call when done with browser automation.")]
    [Parameters("""{"type":"object","properties":{},"required":[]}""")]
    public async Task<object> CloseBrowser(ServiceConfig config, ServiceRequest request)
    {
        await _client.DisposeAsync();

        return new
        {
            Success = true,
            Message = "Browser session closed"
        };
    }

    private async Task<object> WithDiffIfEnabled(ServiceRequest request, Func<Task<(bool success, string url, string title, string message)>> action)
    {
        // Take a pre-action snapshot if diff mode is requested
        PageSnapshot priorSnapshot = null;
        if (request.DiffMode == true && _config.PreferAriaSnapshot)
            priorSnapshot = await _client.TakeSnapshotAsync(_config.DefaultTimeoutMs);

        var (success, url, title, message) = await action();

        if (!success)
            return new { Success = false, Message = message };

        // Compute diff if we have a prior snapshot
        if (priorSnapshot != null)
        {
            var postSnapshot = await _client.TakeSnapshotAsync(_config.DefaultTimeoutMs);
            if (postSnapshot != null)
            {
                var delta = DiffEngine.ComputeDiff(priorSnapshot, postSnapshot);
                if (delta != null && DiffEngine.IsSignificant(delta))
                {
                    return new
                    {
                        Success = true,
                        Url = url,
                        Title = title,
                        Message = message,
                        Delta = new
                        {
                            delta.Added,
                            delta.Removed,
                            Changed = delta.Changed.Select(c => new { c.Ref, c.Was, c.Now }),
                            delta.UnchangedCount
                        }
                    };
                }
            }
        }

        return new { Success = true, Url = url, Title = title, Message = message };
    }
}
