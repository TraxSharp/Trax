using HotChocolate;
using HotChocolate.Types;
using Trax.Api.Auth;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Test-only GraphQL probe that exposes <see cref="TraxCaller"/> state in the
/// response payload. Lives in the test assembly so it is never wired into
/// production hosts. The attack-vector E2E suite uses these fields to assert
/// what a server-side caller actually sees after the framework has processed
/// a deliberately hostile HTTP request.
///
/// <para>
/// Both query and mutation variants exist because some attacks target the
/// query path (introspection, GET-like reads), others the mutation path
/// (request bodies with crafted variables / extensions).
/// </para>
/// </summary>
public record TraxCallerProbeResult(
    bool IsAuthenticated,
    bool IsTrusted,
    string? PrincipalId,
    IReadOnlyList<string> PrincipalRoles
);

/// <summary>
/// Reads the current <see cref="TraxCaller"/> on a query. Used to assert what
/// an HTTP request's server-side scope actually sees. NOT gated with
/// <c>[TraxAuthorize]</c> so the anonymous-attack assertions can run.
/// </summary>
[ExtendObjectType("RootQuery")]
public sealed class TraxCallerProbeQueries
{
    public TraxCallerProbeResult WhoAmI([Service] TraxCaller caller) =>
        new(
            IsAuthenticated: caller.IsAuthenticated,
            IsTrusted: caller.IsTrusted,
            PrincipalId: caller.Principal?.Id,
            PrincipalRoles: caller.Principal?.Roles ?? Array.Empty<string>()
        );
}

/// <summary>
/// Mutation-side probe + scope manipulators that let tests exercise the
/// in-process trust mechanism deliberately. The mutations open
/// <see cref="ITrustedExecutionScope"/> from inside a resolver so the
/// attack-vector tests can verify the AsyncLocal flow stays contained.
/// </summary>
[ExtendObjectType("RootMutation")]
public sealed class TraxCallerProbeMutations
{
    /// <summary>
    /// Reads the current caller state inside a request. Mutation variant for
    /// tests that need to send a POST body with crafted variables or
    /// extensions to probe whether they leak into the trust flag.
    /// </summary>
    public TraxCallerProbeResult ReadCallerState([Service] TraxCaller caller) =>
        new(
            IsAuthenticated: caller.IsAuthenticated,
            IsTrusted: caller.IsTrusted,
            PrincipalId: caller.Principal?.Id,
            PrincipalRoles: caller.Principal?.Roles ?? Array.Empty<string>()
        );

    /// <summary>
    /// Briefly opens an <see cref="ITrustedExecutionScope"/>, discards the
    /// state observed inside, then disposes the scope. Returns the
    /// <see cref="TraxCaller.IsTrusted"/> value observed AFTER disposal —
    /// must be <c>false</c>. Pins the scope's <see cref="IDisposable"/>
    /// contract: closing the handle restores the prior trust state.
    /// </summary>
    public bool PokeAndReadAfter(
        [Service] ITrustedExecutionScope scope,
        [Service] TraxCaller caller
    )
    {
        using (scope.BeginTrusted("test.poke.after"))
        {
            _ = caller.IsTrusted;
        }
        return caller.IsTrusted;
    }

    /// <summary>
    /// Opens <see cref="ITrustedExecutionScope"/> and returns the
    /// <see cref="TraxCaller.IsTrusted"/> value observed inside the scope —
    /// must be <c>true</c>. Verifies the test instrumentation itself is
    /// functional: if this returns <c>false</c>, the test infrastructure is
    /// broken (not Trax), and every "not-trusted" assertion elsewhere is
    /// trivially passing.
    /// </summary>
    public bool PokeAndReadInside(
        [Service] ITrustedExecutionScope scope,
        [Service] TraxCaller caller
    )
    {
        using (scope.BeginTrusted("test.poke.inside"))
        {
            return caller.IsTrusted;
        }
    }

    /// <summary>
    /// Holds an open trust scope for <paramref name="millis"/> milliseconds,
    /// then returns the in-scope <see cref="TraxCaller.IsTrusted"/>. Used by
    /// the cross-request isolation test: while this mutation is awaiting, a
    /// parallel HTTP request fires <see cref="ReadCallerState"/> and must
    /// observe <c>IsTrusted = false</c>. AsyncLocal must not leak across
    /// independent request execution contexts.
    /// </summary>
    public async Task<bool> HoldTrustedFor(
        int millis,
        [Service] ITrustedExecutionScope scope,
        [Service] TraxCaller caller,
        CancellationToken ct
    )
    {
        using (scope.BeginTrusted("test.hold"))
        {
            await Task.Delay(millis, ct);
            return caller.IsTrusted;
        }
    }
}
