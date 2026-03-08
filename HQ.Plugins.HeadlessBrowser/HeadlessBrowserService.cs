using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.HeadlessBrowser.Models;
using Microsoft.Playwright;

namespace HQ.Plugins.HeadlessBrowser;

public class HeadlessBrowserService
{
    private readonly BrowserClient _client;
    private readonly ServiceConfig _config;
    private readonly LogDelegate _logger;

    public HeadlessBrowserService(BrowserClient client, ServiceConfig config, LogDelegate logger)
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
                var response = await page.GotoAsync(request.Url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = config.DefaultTimeoutMs
                });

                var title = await page.TitleAsync();
                var url = page.Url;

                var textContent = await page.EvaluateAsync<string>(
                    "() => document.body?.innerText || ''");

                var summary = textContent.Length > 2000
                    ? textContent[..2000] + "..."
                    : textContent;

                return (object)new
                {
                    Success = true,
                    Title = title,
                    Url = url,
                    StatusCode = response?.Status,
                    ContentSummary = summary.Trim()
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
    }

    [Display(Name = BrowserMethods.GetPageContent)]
    [Description("Get the text or HTML content of the current page or a specific element. Defaults to text content, set contentType to 'html' for raw HTML.")]
    [Parameters("""{"type":"object","properties":{"selector":{"type":"string","description":"CSS selector for a specific element (optional, defaults to full page)"},"contentType":{"type":"string","description":"'text' (default) or 'html'"},"maxLength":{"type":"integer","description":"Maximum content length to return (default 50000)"}},"required":[]}""")]
    public async Task<object> GetPageContent(ServiceConfig config, ServiceRequest request)
    {
        return await _client.ExecuteAsync(async page =>
        {
            var maxLength = request.MaxLength ?? 50000;
            var contentType = request.ContentType?.ToLowerInvariant() ?? "text";
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
                ContentType = contentType,
                Length = content.Length,
                Content = content.Trim()
            };
        });
    }

    [Display(Name = BrowserMethods.GetInteractiveElements)]
    [Description("List interactive elements (links, buttons, inputs, selects, textareas) on the current page with their CSS selectors. Useful for discovering what actions are available.")]
    [Parameters("""{"type":"object","properties":{"elementType":{"type":"string","description":"Filter by element type: 'links', 'buttons', 'inputs', 'all' (default 'all')"},"selector":{"type":"string","description":"Scope search to elements within this CSS selector"}},"required":[]}""")]
    public async Task<object> GetInteractiveElements(ServiceConfig config, ServiceRequest request)
    {
        return await _client.ExecuteAsync(async page =>
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
                Elements = elements
            };
        });
    }

    [Display(Name = BrowserMethods.ClickElement)]
    [Description("Click an element on the page by CSS selector. Waits for the element to be visible and clickable.")]
    [Parameters("""{"type":"object","properties":{"selector":{"type":"string","description":"CSS selector of the element to click"}},"required":["selector"]}""")]
    public async Task<object> ClickElement(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Selector))
            throw new ArgumentException("Missing required parameter: selector");

        return await _client.ExecuteAsync(async page =>
        {
            await page.ClickAsync(request.Selector);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            return (object)new
            {
                Success = true,
                Url = page.Url,
                Title = await page.TitleAsync(),
                Message = $"Clicked element: {request.Selector}"
            };
        });
    }

    [Display(Name = BrowserMethods.FillField)]
    [Description("Fill a form field with a value by CSS selector. Clears the field first, then types the value.")]
    [Parameters("""{"type":"object","properties":{"selector":{"type":"string","description":"CSS selector of the input/textarea to fill"},"value":{"type":"string","description":"The value to type into the field"}},"required":["selector","value"]}""")]
    public async Task<object> FillField(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Selector))
            throw new ArgumentException("Missing required parameter: selector");
        if (request.Value is null)
            throw new ArgumentException("Missing required parameter: value");

        return await _client.ExecuteAsync(async page =>
        {
            await page.FillAsync(request.Selector, request.Value);

            return (object)new
            {
                Success = true,
                Message = $"Filled '{request.Selector}' with value"
            };
        });
    }

    [Display(Name = BrowserMethods.SubmitForm)]
    [Description("Submit a form by clicking a submit button or pressing Enter on a form element. Provide the selector of the submit button or the form itself.")]
    [Parameters("""{"type":"object","properties":{"selector":{"type":"string","description":"CSS selector of the submit button or form element"}},"required":["selector"]}""")]
    public async Task<object> SubmitForm(ServiceConfig config, ServiceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Selector))
            throw new ArgumentException("Missing required parameter: selector");

        return await _client.ExecuteAsync(async page =>
        {
            var tagName = await page.EvaluateAsync<string>(
                "(sel) => document.querySelector(sel)?.tagName?.toLowerCase()", request.Selector);

            if (tagName == "form")
            {
                await page.EvaluateAsync(
                    "(sel) => document.querySelector(sel).submit()", request.Selector);
            }
            else
            {
                await page.ClickAsync(request.Selector);
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            return (object)new
            {
                Success = true,
                Url = page.Url,
                Title = await page.TitleAsync(),
                Message = $"Form submitted via: {request.Selector}"
            };
        });
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
}
