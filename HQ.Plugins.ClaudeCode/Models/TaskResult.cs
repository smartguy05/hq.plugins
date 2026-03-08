namespace HQ.Plugins.ClaudeCode.Models;

public record TaskResult
{
    public bool Success { get; set; }
    public string SessionId { get; set; }
    public string Output { get; set; }
    public string Error { get; set; }
    public long ExitCode { get; set; }
    public string Diff { get; set; }
}
