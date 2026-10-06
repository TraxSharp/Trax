namespace Trax.Api.Auth;

/// <summary>
/// Resolves a verified credential input (API key, validated JWT, OIDC claims, etc.)
/// into a <see cref="TraxPrincipal"/>. Hosts implement this once per auth scheme; the
/// scheme handler calls it from <c>HandleAuthenticateAsync</c> after the transport-layer
/// check has passed.
/// </summary>
/// <typeparam name="TInput">
/// The verified credential type. API-key schemes resolve <see cref="string"/>;
/// JWT schemes resolve a validated token type; OIDC schemes may resolve a claims bag.
/// </typeparam>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Implementations should be cheap or cache aggressively. The resolver runs on
/// every request that presents credentials. Returning <c>null</c> signals that
/// the credential was well-formed but does not map to a known principal; the
/// scheme handler translates this to an authentication failure.
/// </para>
/// </remarks>
public interface ITraxPrincipalResolver<in TInput>
{
    /// <summary>
    /// Resolves the supplied credential to a <see cref="TraxPrincipal"/>, or returns
    /// <c>null</c> to signal "credential format was valid but no matching principal exists."
    /// </summary>
    ValueTask<TraxPrincipal?> ResolveAsync(TInput input, CancellationToken ct);
}
