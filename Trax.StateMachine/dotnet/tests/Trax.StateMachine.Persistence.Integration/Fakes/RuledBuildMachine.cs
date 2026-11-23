using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// <see cref="BuildMachine"/> with its rules declared: entering <c>Building</c> (<c>Build</c>, <c>Retry</c>) requires
/// Operator, and editing or stopping needs only an authenticated caller.
/// </summary>
public sealed class RuledBuildMachine : Machine<BuildState, BuildTrigger>
{
    public const string Id = "ruled-build";

    protected override void Configure(IMachineBuilder<BuildState, BuildTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(BuildState.Draft, () => new JsonObject { ["source"] = "repo" })
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

        m.In(BuildState.Draft)
            .Context<BuildMachine.SourceContext>()
            .On(BuildTrigger.Build)
            .Requires(DeskRoles.Operator)
            .To(BuildState.Building)
            .On(BuildTrigger.Edit)
            .AnyAuthenticated()
            .To(BuildState.Draft);

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
            .OnCancelled(BuildState.BuildCancelled)
            .On(BuildTrigger.StopBuild)
            .AnyAuthenticated()
            .To(BuildState.Draft);

        m.In(BuildState.Built).Context<BuildMachine.BuiltContext>();

        m.In(BuildState.BuildFailed)
            .Context<BuildMachine.SourceContext>()
            .On(BuildTrigger.Retry)
            .Requires(DeskRoles.Operator)
            .To(BuildState.Building)
            .On(BuildTrigger.Edit)
            .AnyAuthenticated()
            .To(BuildState.Draft);

        m.In(BuildState.BuildCancelled)
            .Context<BuildMachine.SourceContext>()
            .On(BuildTrigger.Retry)
            .Requires(DeskRoles.Operator)
            .To(BuildState.Building);
    }

    /// <summary>A build in <paramref name="state"/> with the source context, for seeding through the store.</summary>
    public static Snapshot In(BuildState state) =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = state.ToString(),
            Context = new JsonObject { ["source"] = "repo" },
        };
}
