namespace Trax.Samples.JobHunt.Trains.UpdateProfile;

public record UpdateProfileOutput
{
    public required string UserId { get; init; }
    public required ProfileFacet Facet { get; init; }
    public DateTime UpdatedAt { get; init; }
}
