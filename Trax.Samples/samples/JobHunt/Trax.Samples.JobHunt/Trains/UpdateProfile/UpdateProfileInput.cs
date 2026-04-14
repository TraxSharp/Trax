namespace Trax.Samples.JobHunt.Trains.UpdateProfile;

public enum ProfileFacet
{
    Skills,
    Education,
    WorkHistory,
}

public record UpdateProfileInput
{
    public required string UserId { get; init; }
    public required ProfileFacet Facet { get; init; }
    public required string Json { get; init; }
}
