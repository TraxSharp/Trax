using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Effect.Models.Metadata;

namespace Trax.StateMachine.Persistence;

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
