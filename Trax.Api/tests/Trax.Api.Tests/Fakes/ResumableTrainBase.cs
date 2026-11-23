using System.Globalization;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.Tests.Fakes;

/// <summary>
/// A research chain with a checkpoint, for the operator's resume:
/// <c>AddServices(decider) → GatherSources → Switch&lt;Sources, ResumeVault&gt; → FetchSources →
/// Checkpoint&lt;FetchedSources&gt; → SummarizeSources → PublishSummary</c>. The vault is a
/// sensitive question, so the track taken is withheld from every record, and the checkpoint's
/// state carries <see cref="ResumeProbe.StateSecret"/>, which the input does not, so a surface
/// that showed the stored state would show it. <see cref="ResumeProbe"/> arms a failure.
/// </summary>
/// <remarks>
/// The decider is carried by the chain rather than registered, so the other hosts that scan this
/// assembly need none.
/// </remarks>
public abstract class ResumableTrainBase : ServiceTrain<ResumableInput, string>
{
    private static readonly IDecider Decider = new ScriptedDecider().Choose(
        ResumeVault.WestWing,
        0.9
    );

    protected override Task<Either<Exception, string>> Junctions() =>
        AddServices(Decider)
            .Chain<GatherSources>()
            .Switch<Sources, ResumeVault>(tracks =>
                tracks
                    .When(ResumeVault.EastWing, t => t.Chain<OpenEastWing>())
                    .When(ResumeVault.WestWing, t => t.Chain<OpenWestWing>())
            )
            .Chain<FetchSources>()
            .Checkpoint<FetchedSources>()
            .Chain<SummarizeSources>()
            .Chain<PublishSummary>()
            .Resolve();
}
