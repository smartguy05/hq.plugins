namespace HQ.Plugins.JobBoard.Models;

public record JobListing
{
    public string Id { get; init; }
    public string Title { get; init; }
    public string Company { get; init; }
    public string Location { get; init; }
    public string Description { get; init; }
    public string Salary { get; init; }
    public string JobType { get; init; }
    public string Url { get; init; }
    public string Source { get; init; }
    public string PostedDate { get; init; }
    public string Skills { get; init; }
}
