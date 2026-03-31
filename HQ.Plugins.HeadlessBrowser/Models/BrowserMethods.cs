namespace HQ.Plugins.HeadlessBrowser.Models;

public static class BrowserMethods
{
    public const string NavigateToUrl = "navigate_to_url";
    public const string GetPageContent = "get_page_content";
    public const string GetInteractiveElements = "get_interactive_elements";
    public const string ClickElement = "click_element";
    public const string FillField = "fill_field";
    public const string SubmitForm = "submit_form";
    public const string TakeScreenshot = "take_screenshot";
    public const string ExecuteJavascript = "execute_javascript";
    public const string CloseBrowser = "close_browser";

    // Phase 3: Two-tier retrieval
    public const string GetOutline = "get_outline";
    public const string SearchPage = "search_page";
    public const string GetElement = "get_element";
    public const string GetVisibleText = "get_visible_text";
}
