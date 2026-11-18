using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Types;
using Trax.Api.Auth;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests.AuthE2E;

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
    [TraxAllowAnonymous]
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
    /// must be <see langword="false"/>. Pins the scope's <see cref="IDisposable"/>
    /// contract: closing the handle restores the prior trust state.
    /// </summary>
    [TraxAllowAnonymous]
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
    /// functional: if this returns <see langword="false"/>, the test infrastructure is
    /// broken (not Trax), and every "not-trusted" assertion elsewhere is
    /// trivially passing.
    /// </summary>
    [TraxAllowAnonymous]
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
    [TraxAllowAnonymous]
    public async Task<bool> HoldTrustedFor(
        int millis,
        [Service] ITrustedExecutionScope scope,
        [Service] TraxCaller caller,
        CancellationToken ct
    )
    {
        using (scope.BeginTrusted("test.hold"))
        {
            await Task.Delay(millis, ct).ConfigureAwait(false);
            return caller.IsTrusted;
        }
    }

    /// <summary>
    /// Opens an <see cref="ITrustedExecutionScope"/>, signals the <see cref="TrustedHold"/> named
    /// <paramref name="key"/> that it is open, and holds it until the test releases it, then
    /// returns the in-scope <see cref="TraxCaller.IsTrusted"/>. Lets the cross-request isolation
    /// test fire its parallel request while the scope is known to be open.
    /// </summary>
    [TraxAllowAnonymous]
    public async Task<bool> HoldTrustedUntilReleased(
        string key,
        [Service] ITrustedExecutionScope scope,
        [Service] TraxCaller caller,
        CancellationToken ct
    )
    {
        var hold = TrustedHold.For(key);
        using (scope.BeginTrusted("test.hold"))
        {
            hold.Entered.TrySetResult();
            await hold
                .Released.Task.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System, ct)
                .ConfigureAwait(false);
            return caller.IsTrusted;
        }
    }
}
