namespace Trax.Samples.JobHunt.Trains.ListWatchedCompanies;

public record ListWatchedCompaniesInput
{
    public required string UserId { get; init; }
}
