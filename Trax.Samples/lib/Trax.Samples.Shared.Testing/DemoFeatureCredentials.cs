using System.Security.Cryptography;
using System.Text;
using Trax.Samples.Shared.Auth;
using Trax.StateMachine.Gherkin.Reqnroll;

namespace Trax.Samples.Shared.Testing;

/// <summary>
/// The feature bindings' credential for a sample signing in with a <see cref="DemoTokenIssuer"/>: every person of every
/// scenario is a user of their own, named after the person and the scenario's run, with the first role their actor
/// carries. Two scenarios that both name Ada are two users, so neither sees the other's instances.
/// </summary>
public static class DemoFeatureCredentials
{
    /// <summary>A <see cref="TraxFeatureDriverOptions.Credential"/> minting one bearer token per person per scenario.</summary>
    public static Func<FeatureActor, string, string, FeatureCredential> PerPerson(
        DemoTokenIssuer issuer
    )
    {
        ArgumentNullException.ThrowIfNull(issuer);
        return (actor, person, scenario) =>
        {
            var role =
                actor.Roles.Count > 0
                    ? actor.Roles[0]
                    : throw new InvalidOperationException(
                        $"{actor.Name} has no role, so a demo token cannot be minted for {person}."
                    );
            return new FeatureCredential(
                new Dictionary<string, string>
                {
                    ["Authorization"] = "Bearer " + issuer.Mint(Subject(person, scenario), role),
                }
            );
        };
    }

    /// <summary>The person's name and eight hex digits of a hash of the scenario's run id.</summary>
    public static string Subject(string person, string scenario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(person);
        ArgumentNullException.ThrowIfNull(scenario);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(scenario));
        return $"{person}-{Convert.ToHexStringLower(hash.AsSpan(0, 4))}";
    }
}
