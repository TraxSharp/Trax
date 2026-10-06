using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Decisions;

// Moderating a post. Three questions about the same post are asked in one call: is it a threat,
// what should happen to it, and how severe is it. A threat is taken down whatever the verdict; the
// rest is routed by the verdict, and posts sent for review are routed again by severity.

public sealed record Post(string PostId, string Body);

public sealed record ModerationOutcome(string PostId, string Action);

[Asks(
    "Does this post threaten violence against a person?",
    Yes = "It threatens, or incites, harm to someone.",
    No = "It does not threaten anyone, even if it is rude."
)]
public sealed class ContainsThreat;

[Asks("What should happen to this post under the community rules?")]
public enum Verdict
{
    [System.ComponentModel.Description("Within the rules. Publish it.")]
    Allow,

    [System.ComponentModel.Description("Clearly breaks the rules. Take it down.")]
    Remove,

    [System.ComponentModel.Description("Unclear. A moderator looks at it.")]
    Review,
}

[Asks("How much harm could this post do if it stays up?")]
public enum Severity
{
    [System.ComponentModel.Description("None to speak of.")]
    Low,

    [System.ComponentModel.Description("Upsetting, but not dangerous.")]
    Medium,

    [System.ComponentModel.Description("Could hurt someone.")]
    High,
}

public class ModeratePost(IDecider decider) : Train<Post, ModerationOutcome>
{
    protected override Task<Either<Exception, ModerationOutcome>> Junctions() =>
        AddServices(decider)
            .Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Score<Severity>())
            .Gate<ContainsThreat>(gate =>
                gate.Yes(t => t.Chain<TakeDownPost>(), atLeast: 0.7)
                    .No(t => t.Switch<Verdict>(ModerateByVerdict), below: 0.3)
                    .Unsure(t => t.Chain<PageTrustAndSafety>())
            )
            .Resolve();

    private static Tracks<Post, ModerationOutcome, Verdict> ModerateByVerdict(
        Tracks<Post, ModerationOutcome, Verdict> tracks
    ) =>
        tracks
            .When(Verdict.Allow, t => t.Chain<PublishPost>())
            .When(Verdict.Remove, t => t.Chain<TakeDownPost>())
            .When(
                Verdict.Review,
                t =>
                    t.Scale<Severity>(scale =>
                        scale
                            .AtLeast(Severity.Low, s => s.Chain<QueueForModerator>())
                            .AtLeast(Severity.High, s => s.Chain<PageTrustAndSafety>())
                    )
            );
}

public class PublishPost : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome(input.PostId, "published"));
}

public class TakeDownPost : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome(input.PostId, "taken down"));
}

public class PageTrustAndSafety : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome(input.PostId, "paged trust and safety"));
}

public class QueueForModerator : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome(input.PostId, "queued for a moderator"));
}
