namespace Trax.Samples.PersistedOperations.Trains.Users.LookupUser;

/// <summary>
/// Lightweight user profile returned by the lookup train. Real consumers
/// would project from a database; this sample returns a deterministic
/// fixture so the persisted-op flow is exercised without infrastructure.
/// </summary>
public record UserProfile
{
    public required string UserId { get; init; }
    public required string DisplayName { get; init; }
    public required string Email { get; init; }
    public int LoginCount { get; init; }
}
