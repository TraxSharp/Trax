using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The machine instance whose invoking state queued the run going on, and who owns it. A junction of
/// a train a machine invokes injects this to learn which user it acts for: an invoked run has no
/// caller of its own, and its input is built from the machine's context alone.
/// </summary>
/// <remarks>
/// <para>Resolved when the run starts, from the run's record and the instance's row: the run names
/// the instance that queued it, and the instance names its owner. The run records a hash of the
/// owner, never the user key itself, and only an owner whose key matches that hash is taken, so a
/// second user holding an instance with the same id is never mistaken for the first.</para>
/// <para>Fails closed. Resolving it throws <see cref="InvalidOperationException"/> in a run no
/// machine invoked (a train run directly, queued by a caller, or scheduled), and in an invoked run
/// whose owner cannot be told: the instance was deleted, or its owner no longer matches the hash
/// the run recorded. Inject it only into junctions of trains a machine invokes.</para>
/// </remarks>
/// <param name="Machine">The id of the machine whose state queued the run.</param>
/// <param name="InstanceId">The instance that queued the run.</param>
/// <param name="OwnerKind">Whether a user or the system owns the instance.</param>
/// <param name="UserKey">The owning user's key, or null when the system owns the instance.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/invoked-run">SDK reference</seealso>
public sealed record InvokedRun(
    string Machine,
    Guid InstanceId,
    SnapshotOwnerKind OwnerKind,
    string? UserKey
);

/// <summary>
/// The <see cref="InvokedRun"/> of the run on the current async flow, set by the job runner around the
/// train it starts, which runs in a DI scope of its own, so a scoped value would not reach it.
/// </summary>
internal static class InvokedRunScope
{
    private static readonly AsyncLocal<Resolution?> Current = new();

    /// <summary>What resolving the run's owner came to: the run, or why it has none.</summary>
    internal sealed record Resolution(InvokedRun? Run, string? Refusal);

    /// <summary>Registers <see cref="InvokedRun"/>, resolved from the current flow on every request.</summary>
    internal static void Register(IServiceCollection services) =>
        services.TryAddTransient(_ => Require());

    /// <summary>The run's <see cref="InvokedRun"/>, or a throw saying why there is none.</summary>
    internal static InvokedRun Require() =>
        Current.Value switch
        {
            { Run: { } run } => run,
            { Refusal: { } refusal } => throw new InvalidOperationException(refusal),
            _ => throw new InvalidOperationException(
                "InvokedRun was resolved outside a run a machine invoked. Only a junction of a train a "
                    + "machine's state invokes can learn the instance and its owner this way."
            ),
        };

    /// <summary>
    /// Makes <paramref name="resolution"/> current on this flow until the returned scope is
    /// disposed. Called in the frame that then starts the train: an async local set inside an
    /// awaited method is lost when it returns.
    /// </summary>
    internal static IDisposable Use(Resolution? resolution)
    {
        var previous = Current.Value;
        Current.Value = resolution;
        return new Restore(previous);
    }

    /// <summary>
    /// Resolves <paramref name="run"/>'s invoking instance and owner, or null for a run no machine
    /// invoked.
    /// </summary>
    internal static async Task<Resolution?> Resolve(
        IDataContext context,
        Metadata run,
        CancellationToken cancellationToken
    )
    {
        if (
            run
            is not {
                InvokingMachine: { } machine,
                InvokingInstanceId: { } instance,
                InvokingOwnerKind: { } kind
            }
        )
            return null;

        if (kind == SnapshotOwnerKind.System)
            return new(new InvokedRun(machine, instance, kind, null), null);

        // An instance id is unique per owner only, so every user holding this id is read, and the
        // one whose key the run recorded (hashed) is the owner. A run recorded before the hash was
        // kept is taken only when one user holds the id.
        var keys = await context
            .SnapshotDrafts.AsNoTracking()
            .Where(d =>
                d.Machine == machine && d.Id == instance && d.OwnerKind == SnapshotOwnerKind.User
            )
            .Select(d => d.UserKey)
            .Distinct()
            .ToListAsync(cancellationToken);

        var owners = run.StarterHash is { } starter
            ? keys.Where(k => RunStarter.OfUserKey(k) == starter).ToList()
            : keys;

        return owners is [{ } owner]
            ? new(new InvokedRun(machine, instance, kind, owner), null)
            : new(
                null,
                $"The owner of instance {instance} of '{machine}', which invoked this run, cannot be "
                    + "told: the instance no longer exists, or its owner is not the user it was "
                    + "when it queued the run."
            );
    }

    private sealed class Restore(Resolution? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
