using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectJunction;
using Trax.Mediator.Services.Principal;
using Trax.Samples.Recovery.Corpus;
using Trax.Samples.Recovery.Faults;
using Trax.Samples.Recovery.Records;
using Trax.Samples.Recovery.Trains.Refund;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.Recovery.Trains.StartRun.Junctions;

/// <summary>
/// Opens the run's case file, schedules a one-off manifest that runs now and retries a failed run up
/// to <see cref="MaxRetries"/> times, and arms the crash.
/// </summary>
public class ScheduleDemoRun(
    ITraxScheduler scheduler,
    CaseFiles caseFiles,
    FaultInjector faults,
    ICurrentPrincipalProvider caller,
    IDataContextProviderFactory dataContexts
) : EffectJunction<StartRunInput, StartRunOutput>
{
    /// <summary>Two retries: three attempts in all.</summary>
    public const int MaxRetries = 2;

    public override async Task<StartRunOutput> Run(StartRunInput input)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var externalId = $"recovery-{runId}";

        // The crash is armed beside the run, never in the manifest's input: a retry replays the
        // first attempt's decisions only when the input is byte-identical between attempts. It is
        // armed before the manifest is scheduled, since the dispatcher may start the first run
        // before the start below sees it queued, and disarmed if the start fails, so a refused or
        // timed-out start leaves nothing armed.
        var crash = !input.CrashOnce
            ? CrashPoint.None
            : input.Scenario switch
            {
                Scenario.Refund => input.CrashWhile == RefundCrash.Emailing
                    ? CrashPoint.RefundEmail
                    : CrashPoint.RefundTrack,
                _ => CrashPoint.CoCitation,
            };
        faults.Arm(runId, crash);

        Trax.Effect.Models.Manifest.Manifest manifest;
        string firstRunId;
        try
        {
            manifest = input.Scenario switch
            {
                Scenario.Refund => await ScheduleRefund(
                    runId,
                    externalId,
                    input.OrderId ?? "A-1001"
                ),
                Scenario.TopicMap => await ScheduleTopicMap(runId, externalId, input),
                _ => throw new ArgumentOutOfRangeException(nameof(input), input.Scenario, null),
            };

            // The page follows the first run, so a start that never sees it queued fails here.
            firstRunId = await FirstRunOf(manifest.Id);
        }
        catch
        {
            faults.Disarm(runId);
            throw;
        }

        return new StartRunOutput
        {
            RunId = runId,
            ManifestId = manifest.Id,
            ManifestExternalId = manifest.ExternalId,
            TrainName = manifest.Name,
            ArmedCrash = crash,
            MaxRetries = MaxRetries,
            FirstRunId = firstRunId,
        };
    }

    // The scheduler queues the manifest's first run on its next pass, a second at most in this demo: its id is what
    // the page follows.
    private async Task<string> FirstRunOf(long manifestId)
    {
        var deadline = DateTime.UtcNow + FirstRunWait;
        while (true)
        {
            await using (var context = await dataContexts.CreateDbContextAsync(CancellationToken))
            {
                var queued = await context
                    .WorkQueues.AsNoTracking()
                    .Where(w => w.ManifestId == manifestId)
                    .OrderBy(w => w.Id)
                    .Select(w => w.ExternalId)
                    .FirstOrDefaultAsync(CancellationToken);
                if (queued is not null)
                    return queued;
            }
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"The scheduler did not queue the run within {FirstRunWait.TotalSeconds} seconds."
                );
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken);
        }
    }

    /// <summary>How long a start waits for the scheduler to queue the first run.</summary>
    private static readonly TimeSpan FirstRunWait = TimeSpan.FromSeconds(15);

    private Task<Trax.Effect.Models.Manifest.Manifest> ScheduleRefund(
        string runId,
        string externalId,
        string orderId
    )
    {
        caseFiles.OpenRefund(runId, orderId, caller.GetCurrentPrincipalId());
        return scheduler.ScheduleOnceAsync<IApproveRefundTrain, RefundInput, RefundResult>(
            externalId,
            new RefundInput { RunId = runId, OrderId = orderId },
            TimeSpan.Zero,
            // Every run of the manifest, each retry included, is the caller's to follow.
            options => options.MaxRetries(MaxRetries).StartedBy(caller.GetCurrentPrincipalId())
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
            // Every run of the manifest, each retry included, is the caller's to follow.
            options => options.MaxRetries(MaxRetries).StartedBy(caller.GetCurrentPrincipalId())
        );
    }
}
