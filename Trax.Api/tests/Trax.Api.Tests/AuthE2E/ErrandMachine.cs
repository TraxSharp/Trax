using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Extensions;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// A user's errand: <c>Go</c> enters <c>Running</c>, which invokes <see cref="IProgressMarkerTrain"/>. Registered
/// only by <see cref="RunProgressInvokedRunE2ETests"/>.
/// </summary>
[TraxMachine]
public sealed class ErrandMachine : Machine<ErrandState, ErrandTrigger>
{
    public const string MachineId = "errand";

    protected override void Configure(IMachineBuilder<ErrandState, ErrandTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(ErrandState.Idle, () => new JsonObject());
        m.Start<ErrandStartInput>(s =>
            s.Reduce(Set((ErrandContext c) => c.Note).FromInput((ErrandStartInput i) => i.Note))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(ErrandState.Idle)
            .Context<ErrandContext>()
            .On(ErrandTrigger.Go)
            .AnyAuthenticated()
            .To(ErrandState.Running);

        m.In(ErrandState.Running)
            .Context<ErrandContext>()
            .Invokes<IProgressMarkerTrain, ProgressMarkerInput, ProgressMarkerOutput>(
                ctx => new ProgressMarkerInput { Note = ctx["note"]?.GetValue<string>() ?? "" }
            )
            .OnDone(
                ErrandState.Done,
                reduce: Set((ErrandContext c) => c.Echo)
                    .FromInput((ProgressMarkerOutput o) => o.Echo)
            )
            .OnFailed(ErrandState.Failed)
            .OnCancelled(ErrandState.Cancelled);

        m.In(ErrandState.Done).Context<ErrandContext>();
        m.In(ErrandState.Failed).Context<ErrandContext>();
        m.In(ErrandState.Cancelled).Context<ErrandContext>();
    }
}
