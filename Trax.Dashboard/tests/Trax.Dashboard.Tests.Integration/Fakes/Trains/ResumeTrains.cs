using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Services.Checkpoints;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Dashboard.Tests.Integration.Fakes.Trains;

/// <summary>
/// <c>FetchPages → Checkpoint&lt;FetchedPages&gt; → SummarizePages → PublishPages</c>: a train
/// the run page can offer a resume of. Its junctions never run here.
/// </summary>
public class ResumeGraphTrain : ServiceTrain<ResumeGraphInput, string>, IResumeGraphTrain
{
    public const string Fetch = "FetchPages#0";
    public const string Checkpoint = "Checkpoint<FetchedPages>#0";
    public const string Summarize = "SummarizePages#0";
    public const string Publish = "PublishPages#0";

    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<FetchPages>()
            .Checkpoint<FetchedPages>()
            .Chain<SummarizePages>()
            .Chain<PublishPages>()
            .Resolve();
}

public interface IResumeGraphTrain : IServiceTrain<ResumeGraphInput, string>;

public record ResumeGraphInput
{
    public string Topic { get; init; } = "";
}

public sealed record FetchedPages(string Topic, int Pages);

public sealed record PageSummary(string Text);

public sealed class FetchPages : EffectJunction<ResumeGraphInput, FetchedPages>
{
    public override Task<FetchedPages> Run(ResumeGraphInput input) =>
        Task.FromResult(new FetchedPages(input.Topic, 1));
}

public sealed class SummarizePages : EffectJunction<FetchedPages, PageSummary>
{
    public override Task<PageSummary> Run(FetchedPages input) =>
        Task.FromResult(new PageSummary(input.Topic));
}

public sealed class PublishPages : EffectJunction<PageSummary, string>
{
    public override Task<string> Run(PageSummary input) => Task.FromResult(input.Text);
}

/// <summary>
/// A resume check with set answers: a resume is allowed at the nodes in
/// <see cref="AllowedAt"/>, and after the latest checkpoint when <see cref="AllowsLatest"/>, and
/// refused everywhere else with <see cref="Refusal"/>. The one check the run page's buttons and the
/// operations service both ask, as in a host.
/// </summary>
internal sealed class ScriptedRunResumes : IRunResumes
{
    public const string Refusal = "No checkpoint the run wrote lets it resume there.";

    public HashSet<string> AllowedAt { get; } = new(StringComparer.Ordinal);

    public bool AllowsLatest { get; set; }

    public HashSet<string> Checkpoints { get; } = new(StringComparer.Ordinal);

    public Task<ResumeVerdict> Check(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        string? resumeAt,
        CancellationToken cancellationToken
    ) => Task.FromResult(Verdict(resumeAt));

    public Task<ResumeChecks> CheckMany(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        IReadOnlyCollection<string> points,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult(
            new ResumeChecks(
                Verdict(null),
                points.ToDictionary(p => p, p => Verdict(p)),
                Checkpoints.ToList(),
                Checkpoints.ToList(),
                []
            )
        );

    private ResumeVerdict Verdict(string? point) =>
        (point is null ? AllowsLatest : AllowedAt.Contains(point))
            ? new ResumeVerdict(true, null, null, point, Checkpoints.FirstOrDefault())
            : new ResumeVerdict(false, "no-checkpoint", Refusal, null, null);
}
