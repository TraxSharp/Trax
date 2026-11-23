using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Effect.Models.WorkQueue.DTOs;

namespace Trax.StateMachine.Persistence;

/// <summary>The invoking state a write enters: its train, how the run's input is built, and the owner's cap.</summary>
/// <param name="TrainType">The train's interface.</param>
/// <param name="CreateInput">
/// Builds the run's input from the context the state is entered with and the owner's user key (null for the system).
/// </param>
/// <param name="Limit">
/// The most live invoked runs the entering user may already hold, across every machine, for the entry to be allowed;
/// system owners are not capped.
/// </param>
internal sealed record EnteringInvoke(
    Type TrainType,
    Func<JsonObject, string?, object?> CreateInput,
    int Limit
)
{
    /// <summary>
    /// The child machine the state starts instead of a train, or null when it invokes one. A spawn is not counted
    /// against the owner's live-run cap.
    /// </summary>
    public SpawnDefinition? Spawn { get; init; }
}
