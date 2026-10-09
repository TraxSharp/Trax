using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Records;
using Trax.Samples.Recovery.Trains.Refund;
using Trax.Samples.Recovery.Trains.Research;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.Recovery.Trains.StartRun.Junctions;

/// <summary>
/// Opens the run's case file, schedules a one-off manifest that runs now and retries a failed run up
/// to <see cref="MaxRetries"/> times, and arms the crash.
/// </summary>
public class ScheduleDemoRun(ITraxScheduler scheduler, CaseFiles caseFiles, FaultInjector faults)
    : EffectJunction<StartRunInput, StartRunOutput>
{
    /// <summary>Two retries: three attempts in all.</summary>
    public const int MaxRetries = 2;

    /// <summary>The research topic when the input leaves it out.</summary>
    public const string DefaultTopic = "What the papers say about cold-weather battery wear";

    public override async Task<StartRunOutput> Run(StartRunInput input)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var externalId = $"recovery-{runId}";

        var manifest = input.Scenario switch
        {
            Scenario.Research => await ScheduleResearch(
                runId,
                externalId,
                input.Topic ?? DefaultTopic
            ),
            Scenario.Refund => await ScheduleRefund(runId, externalId, input.OrderId ?? "A-1001"),
            Scenario.TopicMap => await ScheduleTopicMap(runId, externalId, input),
            _ => throw new ArgumentOutOfRangeException(nameof(input), input.Scenario, null),
        };

        // The crash is armed beside the run, never in the manifest's input: a retry replays the
        // first attempt's decisions only when the input is byte-identical between attempts. It is
        // armed only once the run is scheduled, so a refused start leaves nothing armed. The run
        // cannot reach a crash point first: each one comes after a step and a model call.
        var crash = !input.CrashOnce
            ? CrashPoint.None
            : input.Scenario switch
            {
                Scenario.Research => CrashPoint.Report,
                Scenario.Refund => CrashPoint.RefundTrack,
                _ => CrashPoint.CoCitation,
            };
        faults.Arm(runId, crash);

        return new StartRunOutput
        {
            RunId = runId,
            ManifestId = manifest.Id,
            ManifestExternalId = manifest.ExternalId,
            TrainName = manifest.Name,
            ArmedCrash = crash,
            MaxRetries = MaxRetries,
        };
    }

    private Task<Trax.Effect.Models.Manifest.Manifest> ScheduleResearch(
        string runId,
        string externalId,
        string topic
    )
    {
        if (string.IsNullOrWhiteSpace(topic) || topic.Length > 200)
            throw new ArgumentException("A topic is 1 to 200 characters.", nameof(topic));

        caseFiles.OpenResearch(runId);
        return scheduler.ScheduleOnceAsync<IResearchTopicTrain, ResearchInput, ResearchReport>(
            externalId,
            new ResearchInput { RunId = runId, Topic = topic.Trim() },
            TimeSpan.Zero,
            options => options.MaxRetries(MaxRetries)
        );
    }

    private Task<Trax.Effect.Models.Manifest.Manifest> ScheduleRefund(
        string runId,
        string externalId,
        string orderId
    )
    {
        caseFiles.OpenRefund(runId, orderId);
        return scheduler.ScheduleOnceAsync<IApproveRefundTrain, RefundInput, RefundResult>(
            externalId,
            new RefundInput { RunId = runId, OrderId = orderId },
            TimeSpan.Zero,
            options => options.MaxRetries(MaxRetries)
        );
    }

    private Task<Trax.Effect.Models.Manifest.Manifest> ScheduleTopicMap(
        string runId,
        string externalId,
        StartRunInput input
    )
    {
        var fields = input.Fields is { Count: > 0 } chosen ? chosen : CorpusFixture.Fields;
        var unknown = fields.Except(CorpusFixture.Fields).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException(
                $"The corpus has no field {string.Join(", ", unknown)}.",
                nameof(input)
            );

        return scheduler.ScheduleOnceAsync<IBuildTopicMapTrain, TopicMapInput, TopicMap>(
            externalId,
            new TopicMapInput
            {
                RunId = runId,
                Fields = fields.Distinct().ToList(),
                FromYear = input.FromYear ?? 2016,
                ToYear = input.ToYear ?? 2025,
            },
            TimeSpan.Zero,
            options => options.MaxRetries(MaxRetries)
        );
    }
}
