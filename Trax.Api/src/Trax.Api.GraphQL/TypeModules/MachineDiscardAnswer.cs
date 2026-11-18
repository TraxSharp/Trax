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

/// <summary>What <c>discard</c> answers.</summary>
/// <param name="Id">The instance's id.</param>
/// <param name="Discarded">Whether the instance is gone.</param>
/// <param name="Problem">Why the discard was refused, or null.</param>
internal sealed record MachineDiscardAnswer(Guid Id, bool Discarded, SnapshotProblem? Problem);
