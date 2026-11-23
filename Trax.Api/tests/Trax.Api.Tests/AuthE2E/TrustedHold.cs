using System.Collections.Concurrent;
using HotChocolate;
using HotChocolate.Types;
using Trax.Api.Auth;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// The handshake between a test and <see cref="TraxCallerProbeMutations.HoldTrustedUntilReleased"/>,
/// keyed so parallel tests never share one: <see cref="Entered"/> completes once the scope is
/// open, and the scope stays open until the test completes <see cref="Released"/>.
/// </summary>
public sealed class TrustedHold
{
    private static readonly ConcurrentDictionary<string, TrustedHold> Holds = new(
        StringComparer.Ordinal
    );

    /// <summary>The hold named <paramref name="key"/>, created on first use.</summary>
    public static TrustedHold For(string key) => Holds.GetOrAdd(key, _ => new TrustedHold());

    /// <summary>Completed by the mutation once its trusted scope is open.</summary>
    public TaskCompletionSource Entered { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completed by the test to let the mutation close its scope and answer.</summary>
    public TaskCompletionSource Released { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
