namespace Trax.Samples.JobHunt.Trains.WatchCompany;

public record WatchCompanyInput
{
    public required string UserId { get; init; }
    public required string CompanyName { get; init; }
    public required string CareersUrl { get; init; }
}
