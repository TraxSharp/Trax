using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Core.Utils;
using Trax.Effect.Services.JunctionEvents;

namespace Trax.Effect.Services.Checkpoints;

/// <inheritdoc />
internal sealed class RunResumes(IEnumerable<ICheckpointRows> rows, IServiceProvider services)
    : IRunResumes
{
    public async Task<ResumeVerdict> Check(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        string? resumeAt,
        CancellationToken cancellationToken
    )
    {
        if (rows.FirstOrDefault() is not { } store)
            return ResumePlanner.NoRows;

        if (await Done(store, runId, cancellationToken).ConfigureAwait(false) is { } done)
            return done;

        var lineage = await store.Lineage(runId, cancellationToken).ConfigureAwait(false);

        return ResumePlanner
            .Check(
                chain,
                ChainGraph.From(chain, train, input, output).Hash,
                input,
                output,
                services.GetService<IServiceProviderIsService>(),
                ResumePlanner.Chosen(chain, lineage),
                resumeAt
            )
            .Verdict;
    }

    public async Task<ResumeChecks> CheckMany(
        Type train,
        ChainRecorder chain,
        Type input,
        Type output,
        long runId,
        IReadOnlyCollection<string> points,
        CancellationToken cancellationToken
    )
    {
        if (rows.FirstOrDefault() is not { } store)
            return new ResumeChecks(
                ResumePlanner.NoRows,
                points
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(p => p, _ => ResumePlanner.NoRows, StringComparer.Ordinal),
                [],
                [],
                []
            );

        // One lineage read for every point, without any row's state: a verdict needs only the
        // rows' nodes, hashes and fingerprints.
        var lineage = await store.Lineage(runId, cancellationToken).ConfigureAwait(false);
        var hash = ChainGraph.From(chain, train, input, output).Hash;
        var container = services.GetService<IServiceProviderIsService>();
        var chosen = ResumePlanner.Chosen(chain, lineage);
        var done = await Done(store, runId, cancellationToken).ConfigureAwait(false);

        ResumeVerdict At(string? point) =>
            done
            ?? ResumePlanner.Check(chain, hash, input, output, container, chosen, point).Verdict;

        var (restored, restoredTracks) = ResumePlanner.Resumed(
            chain,
            input,
            output,
            container,
            lineage
        );

        return new ResumeChecks(
            At(point: null),
            points
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(p => p, p => At(p), StringComparer.Ordinal),
            chosen.Keys.ToList(),
            lineage.Count > 0 ? lineage[0].Rows.Select(r => r.NodeId).Distinct().ToList() : [],
            restored
        )
        {
            RestoredTracks = restoredTracks,
        };
    }

    /// <summary>
    /// The refusal for a run a resume of which already completed, whose work is done, at any
    /// point; null for any other run.
    /// </summary>
    private static async Task<ResumeVerdict?> Done(
        ICheckpointRows store,
        long runId,
        CancellationToken cancellationToken
    ) =>
        await store.Source(runId, cancellationToken).ConfigureAwait(false)
            is { ResumedToCompletion: true }
            ? new ResumeVerdict(
                CanResume: false,
                ResumeSources.AlreadyResumed,
                "A" + ResumeSources.AlreadyResumedReason(runId)[1..],
                Target: null,
                Checkpoint: null
            )
            : null;
}
