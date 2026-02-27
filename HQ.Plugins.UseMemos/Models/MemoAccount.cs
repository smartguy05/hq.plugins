namespace HQ.Plugins.UseMemos.Models;

public record MemoAccount
{
    public string ApiKey { get; set; }
    public string MemosUrl { get; set; }
}