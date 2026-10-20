using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public sealed record BuildInput(string Source);

/// <summary>A pointer to what the build produced, never the artifact itself.</summary>
public sealed record BuildOutput
{
    public string Artifact { get; init; } = "";
}

/// <summary>The train <see cref="BuildMachine"/> invokes, named by its interface. These tests never run it.</summary>
public interface IBuildTrain : IServiceTrain<BuildInput, BuildOutput>;

public enum BuildState
{
    Draft,
    Building,
    Built,
    BuildFailed,
    BuildCancelled,
}

public enum BuildTrigger
{
    Build,
    StopBuild,
    Retry,
    Edit,
}

/// <summary>
/// A user's wizard whose <c>Building</c> state invokes <see cref="IBuildTrain"/>: a user enters it with <c>Build</c>
/// and leaves it with <c>StopBuild</c>; its outcome goes to <c>Built</c>, <c>BuildFailed</c> or
/// <c>BuildCancelled</c>, and a failed build is retried by entering <c>Building</c> again.
/// </summary>
public sealed class BuildMachine : Machine<BuildState, BuildTrigger>
{
    public sealed record SourceContext
    {
        [MinLength(1)]
        public string Source { get; init; } = "";
    }

    public sealed record BuiltContext
    {
        [MinLength(1)]
        public string Source { get; init; } = "";

        [MinLength(1)]
        public string Artifact { get; init; } = "";
    }

    public const string Id = "build-wizard";

    protected override void Configure(IMachineBuilder<BuildState, BuildTrigger> m)
    {
        m.Id(BuildMachine.Id)
            .Version(1)
            .StartsAt(BuildState.Draft, () => new JsonObject { ["source"] = "repo" });

        m.In(BuildState.Draft)
            .Context<SourceContext>()
            .On(BuildTrigger.Build)
            .To(BuildState.Building)
            .On(BuildTrigger.Edit)
            .To(BuildState.Draft);

        m.In(BuildState.Building)
            .Context<SourceContext>()
            .Invokes<IBuildTrain, BuildInput, BuildOutput>(ctx => new BuildInput(
                ctx["source"]!.GetValue<string>()
            ))
            .OnDone(
                BuildState.Built,
                reduce: Set((BuiltContext c) => c.Artifact).FromInput((BuildOutput o) => o.Artifact)
            )
            .OnFailed(BuildState.BuildFailed)
            .OnCancelled(BuildState.BuildCancelled)
            .On(BuildTrigger.StopBuild)
            .To(BuildState.Draft);

        m.In(BuildState.Built).Context<BuiltContext>();

        m.In(BuildState.BuildFailed)
            .Context<SourceContext>()
            .On(BuildTrigger.Retry)
            .To(BuildState.Building)
            .On(BuildTrigger.Edit)
            .To(BuildState.Draft);

        m.In(BuildState.BuildCancelled)
            .Context<SourceContext>()
            .On(BuildTrigger.Retry)
            .To(BuildState.Building);
    }

    /// <summary>A snapshot of this machine as the client sends it.</summary>
    public static string Json(string state, JsonObject context) =>
        new JsonObject
        {
            ["machine"] = BuildMachine.Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = context.DeepClone(),
        }.ToJsonString();
}
