using Trax.Samples.Shared.Auth;

namespace Trax.Samples.CheckIn.Api.Auth;

/// <summary>
/// The demo's sign-in, Development only (ADR <c>Trax.Docs/adr/0035</c>): a token for one person, named and given one
/// check-in role, signed with a key published in this repository. The page asks for one per person, so no person's
/// credential is in another's page, and the feature tests mint one per person per scenario. The key carries
/// <c>do-not-use-in-production</c>, so a host using it refuses to start outside Development.
/// </summary>
public static class DemoTokens
{
    public const string SigningKeyText = "checkin-sample-signing-key-do-not-use-in-production";

    public static readonly DemoTokenIssuer Issuer = new(
        issuer: "trax-samples-checkin-dev",
        audience: "trax-samples-checkin",
        signingKeyText: SigningKeyText,
        roles: CheckInRoles.All
    );
}
