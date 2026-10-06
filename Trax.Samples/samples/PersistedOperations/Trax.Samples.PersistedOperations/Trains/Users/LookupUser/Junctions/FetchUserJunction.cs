using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.PersistedOperations.Trains.Users.LookupUser.Junctions;

/// <summary>
/// Resolves a user profile. In a real app this would be an EF Core query;
/// the sample returns a deterministic fixture so the persisted-op flow is
/// exercised without seed data.
/// </summary>
public class FetchUserJunction(ILogger<FetchUserJunction> logger)
    : Junction<LookupUserInput, UserProfile>
{
    public override Task<UserProfile> Run(LookupUserInput input)
    {
        logger.LogInformation("Looking up user {UserId}", input.UserId);

        // Deterministic synthetic profile keyed on UserId.
        var hash = (uint)input.UserId.GetHashCode();
        return Task.FromResult(
            new UserProfile
            {
                UserId = input.UserId,
                DisplayName = $"User {input.UserId}",
                Email = $"{input.UserId}@example.test",
                LoginCount = (int)(hash % 1000),
            }
        );
    }
}
