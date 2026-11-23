using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// One event of a machine's push (<c>&lt;machine&gt;Changed</c>): the instance's whole snapshot with its revision, or
/// the end of the stream (<see cref="Gone"/>, or a <see cref="Problem"/>).
/// </summary>
/// <param name="RowId">The row the stream follows (the instance's incarnation), or null on a refusal.</param>
/// <param name="Revision">The snapshot's revision, or null when the event carries no snapshot.</param>
/// <param name="State">The state the instance is in, or null when the event carries no snapshot.</param>
/// <param name="Context">The instance's context, read by the machine's context type, or null without a snapshot.</param>
/// <param name="Gone">Whether the instance was deleted or expired. The last event of the stream.</param>
/// <param name="Problem">Why the stream was refused or ended, or null.</param>
internal sealed record MachineChange(
    long? RowId,
    long? Revision,
    string? State,
    JsonObject? Context,
    bool Gone,
    SnapshotProblem? Problem
)
{
    /// <summary>When the timer of the state the instance is in fires, or null.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// Whether the client must read the instance again because this event cannot stand for it. Every event this
    /// server sends is a whole snapshot, so it never sets it; a client honours it all the same.
    /// </summary>
    public bool Reload { get; init; }

    /// <summary>The terminal answer that carries only a problem, <paramref name="code"/> and <paramref name="message"/>.</summary>
    public static MachineChange Refused(long? rowId, RefusalCode code, string message) =>
        new(
            rowId,
            Revision: null,
            State: null,
            Context: null,
            Gone: false,
            new SnapshotProblem { Code = code, Message = message }
        );
}
