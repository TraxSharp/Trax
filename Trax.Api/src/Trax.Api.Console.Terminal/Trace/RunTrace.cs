using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Lists;
using Trax.Api.Console.Terminal.Operations;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Trace;

/// <summary>What one line of a run's trace is.</summary>
internal enum TraceKind
{
    /// <summary>A step: a junction, a decision point or a route, as it started and ended.</summary>
    Step,

    /// <summary>A decision the run recorded: the question, the answer, who answered.</summary>
    Decision,

    /// <summary>A line the run logged.</summary>
    Log,
}

/// <summary>One line of a run's trace.</summary>
/// <param name="Kind">A step, a decision or a log line.</param>
/// <param name="Key">What makes it this line: a step's position, a decision's or log's id. A step that changes keeps its key.</param>
/// <param name="At">When it happened; null for a log line, which the host records without a time.</param>
/// <param name="Text">The line as the terminal shows it.</param>
/// <param name="Json">What the host answered for it.</param>
/// <param name="Ended">For a step, whether it had ended (completed, failed or cancelled) when it was read.</param>
internal sealed record TraceEntry(
    TraceKind Kind,
    string Key,
    DateTimeOffset? At,
    string Text,
    JsonNode? Json,
    bool Ended = false
)
{
    /// <summary>
    /// Of two readings of one line, the one to keep: a step that had ended is newer than one still running, and of two
    /// equally far along, <paramref name="later"/>, the one read last.
    /// </summary>
    public static TraceEntry Newest(TraceEntry earlier, TraceEntry later) =>
        earlier.Ended && !later.Ended ? earlier : later;
}

/// <summary>
/// A run's steps, decisions and log, read with the browser console's documents and told as one timeline: steps and
/// decisions in time order, then the log in the order it was written.
/// </summary>
internal static class RunTrace
{
    /// <summary>The most of each the trace reads; the host clamps steps at 500 too.</summary>
    public const int Take = 500;

    /// <summary>Reads the run's trace as it stands now.</summary>
    public static async Task<TraxAnswer<IReadOnlyList<TraceEntry>>> LoadAsync(
        TraxClient client,
        long run,
        CancellationToken cancellationToken
    )
    {
        var steps = await client.JunctionRunsAsync(
            new JunctionRunsVariables { MetadataId = run, Take = Take },
            cancellationToken
        );
        if (!steps.Succeeded)
            return Failed(steps);
        var decisions = await client.DecisionsAsync(
            new DecisionsVariables { MetadataId = run, Take = Take },
            cancellationToken
        );
        if (!decisions.Succeeded)
            return Failed(decisions);
        var logs = await client.LogsAsync(
            new LogsVariables
            {
                Take = Take,
                MetadataId = run,
                Order = SortOrder.Oldest,
            },
            cancellationToken
        );
        if (!logs.Succeeded)
            return Failed(logs);

        var timeline = (steps.Data!.Operations?.JunctionRuns ?? [])
            .Select(Step)
            .Concat((decisions.Data!.Operations?.Decisions.Items ?? []).Select(Decision))
            .OrderBy(e => e.At)
            .ThenBy(e => e.Kind)
            .Concat((logs.Data!.Operations?.Logs.Logs.Items ?? []).Select(Log))
            .ToList();
        return new() { Data = timeline };
    }

    /// <summary>
    /// Follows the run's steps as they start and end (<c>onJunctionEvent</c>). The stream is lossy under load: when
    /// <paramref name="onGap"/> is called, events were missed and the trace should be read again.
    /// </summary>
    public static IDisposable Follow(
        TraxClient client,
        long run,
        Action<TraceEntry> onStep,
        Action onGap,
        Action<string> onEnded
    )
    {
        long? last = null;
        return client.OnJunctionEvent(
            new OnJunctionEventVariables { MetadataId = run },
            e =>
            {
                switch (e)
                {
                    case TraxStreamEvent<OnJunctionEventResult>.Next next:
                        var change = next.Data.OnJunctionEvent;
                        if (last is { } seen && change.Sequence > seen + 1)
                            onGap();
                        last = change.Sequence;
                        onStep(StepOf(change.Junction));
                        break;
                    case TraxStreamEvent<OnJunctionEventResult>.Resumed:
                        onGap();
                        break;
                    case TraxStreamEvent<OnJunctionEventResult>.Ended ended:
                        onEnded(
                            ended.Failure?.Message
                                ?? string.Join("; ", ended.Errors.Select(x => x.Message)) switch
                                {
                                    "" => "The host ended the stream.",
                                    var m => m,
                                }
                        );
                        break;
                }
            }
        );
    }

    private static TraceEntry Step(JunctionRunsResultOperationsJunctionRuns s) =>
        new(
            TraceKind.Step,
            $"step:{s.Position}",
            s.StartedAt,
            StepText(
                Wire.Name(s.Kind),
                s.Name,
                Wire.Name(s.State),
                s.DurationMs,
                s.Answer,
                s.FailureException
            ),
            Wire.Json(s),
            s.State != JunctionRunState.InProgress
        );

    private static TraceEntry StepOf(OnJunctionEventResultOnJunctionEventJunction s) =>
        new(
            TraceKind.Step,
            $"step:{s.Position}",
            s.StartedAt,
            StepText(
                Wire.Name(s.Kind),
                s.Name,
                Wire.Name(s.State),
                s.DurationMs,
                s.Answer,
                s.FailureException
            ),
            Wire.Json(s),
            s.State != JunctionRunState.InProgress
        );

    private static string StepText(
        string kind,
        string name,
        string state,
        double? durationMs,
        string? answer,
        string? failure
    ) =>
        string.Join(
            "  ",
            new[]
            {
                $"{kind.ToLowerInvariant()} {name}",
                state,
                durationMs is { } ms ? $"{ms:0} ms" : null,
                answer is null ? null : $"answer {answer}",
                failure,
            }.Where(p => p is not null)
        );

    private static TraceEntry Decision(DecisionsResultOperationsDecisionsItems d) =>
        new(
            TraceKind.Decision,
            $"decision:{d.Id}",
            d.DecidedAt,
            string.Join(
                "  ",
                new[]
                {
                    $"decision {d.QuestionKey ?? "?"}",
                    d.IsRefused ? $"refused {d.Refused}" : $"answer {d.Answer}",
                    d.Decider is null ? null : $"by {d.Decider}",
                    d.Replayed ? "replayed" : null,
                }.Where(p => p is not null)
            ),
            Wire.Json(d)
        );

    private static TraceEntry Log(LogsResultOperationsLogsLogsItems l) =>
        new(
            TraceKind.Log,
            $"log:{l.Id}",
            null,
            $"{Wire.Name(l.Level).ToLowerInvariant()}  {l.Category}  {l.Message}"
                + (l.Exception is null ? "" : $"  {l.Exception}"),
            Wire.Json(l)
        );

    private static TraxAnswer<IReadOnlyList<TraceEntry>> Failed<T>(TraxAnswer<T> answer) =>
        new() { Errors = answer.Errors, Failure = answer.Failure };
}
