using Trax.Api.Auth;

namespace Trax.Samples.JobHunt.Auth;

/// <summary>
/// Resolves demo API keys to JobHunt user principals. Demonstrates the class
/// form of <see cref="ITraxPrincipalResolver{String}"/> that the Trax API-key
/// scheme accepts via <c>services.AddTraxApiKeyAuth&lt;JobHuntApiKeyResolver&gt;()</c>.
/// </summary>
/// <remarks>
/// Demo only. NO WARRANTY. See <c>Trax.Api/SECURITY-DISCLAIMER.md</c>.
/// </remarks>
public sealed class JobHuntApiKeyResolver : ITraxPrincipalResolver<string>
{
    private static readonly IReadOnlyDictionary<string, (string Id, string DisplayName)> Users =
        new Dictionary<string, (string, string)>
        {
            [ApiKeyDefaults.AliceKey] = ("alice", "Alice"),
            [ApiKeyDefaults.BobKey] = ("bob", "Bob"),
            [ApiKeyDefaults.CharlieKey] = ("charlie", "Charlie"),
        };

    public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct)
    {
        if (!Users.TryGetValue(input, out var user))
            return ValueTask.FromResult<TraxPrincipal?>(null);

        return ValueTask.FromResult<TraxPrincipal?>(
            new TraxPrincipal(user.Id, user.DisplayName, ["User"], PrincipalType: "apikey")
        );
    }
}
