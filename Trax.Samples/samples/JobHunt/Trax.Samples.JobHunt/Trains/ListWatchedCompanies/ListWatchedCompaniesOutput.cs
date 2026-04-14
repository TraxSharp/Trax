namespace Trax.Samples.JobHunt.Trains.ListWatchedCompanies;

public record WatchedCompanySummary
{
    public Guid Id { get; init; }
    public required string CompanyName { get; init; }
    public required string CareersUrl { get; init; }
    public DateTime? LastCheckedAt { get; init; }
}

public record ListWatchedCompaniesOutput
{
    public required List<WatchedCompanySummary> Companies { get; init; }
}
