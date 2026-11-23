using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Core.Utils;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Services.Checkpoints;

/// <summary>
/// Decides whether a failed run can resume, from its checkpoints and its train's declared chain,
/// without running anything. The operator's resume and the scheduler's retries ask it before they
/// queue a resumed run; the resumed run asks again when it starts.
/// </summary>
/// <remarks>See Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md.</remarks>
internal interface IRunResumes
{
    /// <summary>
    /// Whether the run <paramref name="runId"/> of <paramref name="train"/> can resume at
    /// <paramref name="resumeAt"/>, or after its latest checkpoint when that is null.
    /// </summary>
    /// <param name="train">The train's class.</param>
    /// <param name="chain">Its declared chain.</param>
    /// <param name="input">Its input type.</param>
    /// <param name="output">Its output type.</param>
    /// <param name="runId">The run to resume.</param>
    /// <param name="resumeAt">The step to resume at, or null.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<ResumeVerdict> Check(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        string? resumeAt,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Whether the run <paramref name="runId"/> of <paramref name="train"/> can resume after its
    /// latest checkpoint and at each of <paramref name="points"/>, from one read of its lineage,
    /// with the nodes that hold a checkpoint it can resume from and, when it is itself a resumed
    /// run, the nodes it restored rather than ran. What an operator's run graph shows; each verdict
    /// is the one <see cref="Check"/> gives for that point.
    /// </summary>
    /// <remarks>
    /// An implementation that predates it asks <see cref="Check"/> once per point and reports no
    /// checkpoints and no restored nodes.
    /// </remarks>
    /// <param name="train">The train's class.</param>
    /// <param name="chain">Its declared chain.</param>
    /// <param name="input">Its input type.</param>
    /// <param name="output">Its output type.</param>
    /// <param name="runId">The run.</param>
    /// <param name="points">The node ids to check a resume at.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    async Task<ResumeChecks> CheckMany(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        IReadOnlyCollection<string> points,
        CancellationToken cancellationToken
    )
    {
        var latest = await Check(
                train,
                chain,
                input,
                output,
                runId,
                resumeAt: null,
                cancellationToken
            )
            .ConfigureAwait(false);
        var at = new Dictionary<string, ResumeVerdict>(StringComparer.Ordinal);

        foreach (var point in points)
            at[point] = await Check(train, chain, input, output, runId, point, cancellationToken)
                .ConfigureAwait(false);

        return new ResumeChecks(latest, at, [], [], []);
    }
}
