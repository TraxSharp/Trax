// What the example on sdk-reference/train-methods/decide.md names but does not declare: the post,
// the questions asked about it, and the junctions on each track.
using Trax.Core.Decisions;
using Trax.Core.Junction;

public record Post(string Id, string Body);

public record ModerationOutcome(string Action);

[Asks("Does the post threaten someone?")]
public sealed class ContainsThreat;

[Asks("What should happen to the post?")]
public enum Verdict
{
    Publish,
    Hide,
}

public enum Severity
{
    Low,
    Medium,
    High,
}

public class TakeDownPost : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome("removed"));
}

public class PublishPost : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome("published"));
}

public class HidePost : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome("hidden"));
}

public class PageTrustAndSafety : Junction<Post, ModerationOutcome>
{
    public override Task<ModerationOutcome> Run(Post input) =>
        Task.FromResult(new ModerationOutcome("escalated"));
}
