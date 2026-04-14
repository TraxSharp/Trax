namespace Trax.Samples.JobHunt.Trains.WatchCompany;

public record WatchCompanyOutput
{
    public Guid WatchedCompanyId { get; init; }
    public required string CompanyName { get; init; }
}
