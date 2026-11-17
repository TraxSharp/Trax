using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// A build that starts in its invoking state, so a start queues the run, with at most one live run per owner.
/// </summary>
public sealed class InvokingStartMachine : Machine<BuildState, BuildTrigger>
{
    public const string Id = "invoking-start";

    protected override void Configure(IMachineBuilder<BuildState, BuildTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(BuildState.Building, () => new JsonObject { ["source"] = "repo" })
            .InvokedRunLimit(1)
            .Start<BuildStart>(s =>
                s.Reduce(
                        Set((BuildMachine.SourceContext c) => c.Source)
                            .FromInput((BuildStart i) => i.Source)
                    )
                    .AnyAuthenticated()
            )
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(BuildState.Building)
            .Context<BuildMachine.SourceContext>()
            .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput(
                ctx["source"].NotNull().GetValue<string>()
            ))
            .OnDone(
                BuildState.Built,
                reduce: Set((BuildMachine.BuiltContext c) => c.Artifact)
                    .FromInput((BuildOutput o) => o.Artifact)
            )
            .OnFailed(BuildState.BuildFailed)
            .OnCancelled(BuildState.BuildCancelled);

        m.In(BuildState.Built).Context<BuildMachine.BuiltContext>();
        m.In(BuildState.BuildFailed).Context<BuildMachine.SourceContext>();
        m.In(BuildState.BuildCancelled).Context<BuildMachine.SourceContext>();
    }
}
