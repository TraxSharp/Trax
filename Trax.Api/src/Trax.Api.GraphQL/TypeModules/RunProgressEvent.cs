using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading.Channels;
using HotChocolate;
using HotChocolate.Authorization;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Subscriptions;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.Types;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>One event of a followed run: a step, a change of state, or the end of the stream.</summary>
/// <param name="RunId">The run's external id.</param>
/// <param name="Sequence">The event's number in this stream; a skipped number means events were lost, so read the run again.</param>
/// <param name="State">The run's new state, on a change of state; else null.</param>
/// <param name="Step">The step, on a step; else null.</param>
/// <param name="Failure">Why the run failed, when a junction threw a <c>TrainException</c>.</param>
/// <param name="Problem">Why the stream was refused, or null. The last event of the stream.</param>
/// <param name="RetriedAs">The id of the run that retries this failed one, when it is queued. The last event of the stream.</param>
internal sealed record RunProgressEvent(
    string RunId,
    long Sequence,
    RunProgressState? State,
    JunctionStep? Step,
    string? Failure,
    SnapshotProblem? Problem,
    string? RetriedAs = null
);
