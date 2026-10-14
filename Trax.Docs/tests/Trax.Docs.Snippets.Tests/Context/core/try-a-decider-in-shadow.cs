// What the examples on core/try-a-decider-in-shadow.md take as given: a post, the verdict asked
// about it, and the junctions each track runs.
using System.ComponentModel;
using Trax.Core.Decisions;
using Trax.Core.Junction;

public sealed record Post(string PostId, string Body);

public sealed record ModerationOutcome(string PostId, string Action);

[Asks("What should happen to this post under the community rules?")]
public enum Verdict
{
    [Description("Within the rules. Publish it.")]
    Allow,

    [Description("Clearly breaks the rules. Take it down.")]
    Remove,

    [Description("Unclear. A moderator looks at it.")]
    Review,
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

public class QueueForModerator : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome(input.PostId, "queued for a moderator"));
}
