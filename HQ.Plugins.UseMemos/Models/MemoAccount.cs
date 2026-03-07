using HQ.Models.Attributes;

namespace HQ.Plugins.UseMemos.Models;

public record MemoAccount
{
    [Tooltip("API key for authenticating with the Memos instance")]
    public string ApiKey { get; set; }

    [Tooltip("Base URL of your Memos instance, e.g. https://memos.example.com")]
    public string MemosUrl { get; set; }
}
