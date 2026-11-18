using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>What a typed machine operation answers: the server's snapshot with its revision, or a problem.</summary>
/// <param name="Id">The instance's id; the requested one when the operation was refused.</param>
/// <param name="Revision">The instance's revision after the operation, or null when refused.</param>
/// <param name="State">The state the instance is in, or null when refused.</param>
/// <param name="Context">The instance's context, or null when refused.</param>
/// <param name="Problem">Why the operation was refused, or null when it was not.</param>
internal sealed record MachineAnswer(
    Guid? Id,
    long? Revision,
    string? State,
    JsonObject? Context,
    SnapshotProblem? Problem
)
{
    /// <summary>When the timer of the state the instance is in fires, or null.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// False when the answer replays a request the instance has moved past, whose due time the server cannot know.
    /// </summary>
    public bool DueAtKnown { get; init; } = true;

    /// <summary>A refusal of the operation on <paramref name="id"/>.</summary>
    public static MachineAnswer Refused(Guid? id, RefusalCode code, string message) =>
        new(
            id,
            Revision: null,
            State: null,
            Context: null,
            new SnapshotProblem { Code = code, Message = message }
        );

    /// <summary>A refusal of the operation on <paramref name="id"/>.</summary>
    public static MachineAnswer Refused(Guid? id, SnapshotProblem problem) =>
        new(id, Revision: null, State: null, Context: null, problem);

    /// <summary>The answer holding the canonical snapshot <paramref name="wire"/> the server stored.</summary>
    public static MachineAnswer Of(
        Guid? id,
        string wire,
        long? revision,
        DateTimeOffset? dueAt,
        bool dueAtKnown = true
    )
    {
        var snapshot = JsonNode.Parse(wire) as JsonObject;
        return new MachineAnswer(
            id,
            revision,
            snapshot?["state"]?.GetValue<string>(),
            snapshot?["context"] as JsonObject ?? [],
            Problem: null
        )
        {
            DueAt = dueAt,
            DueAtKnown = dueAtKnown,
        };
    }
}
