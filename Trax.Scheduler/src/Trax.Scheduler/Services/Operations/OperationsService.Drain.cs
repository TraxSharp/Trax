using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Services.Drain;
using Trax.Effect.Services.ChangeSignal;

namespace Trax.Scheduler.Services.Operations;

// The drain (Trax.Docs/adr/0065): the console's Drain and Resume and the API's
// operations.config.drain and resume call these, and nothing else writes the row from a request.
public partial class OperationsService
{
    /// <summary>How long a drain set from the console or the API lasts when the caller names no time: 20 minutes.</summary>
    public static readonly TimeSpan DefaultDrainDuration =
        TraxDrain.DefaultTimeout + TraxDrain.Margin;

    /// <summary>The shortest drain the console or the API sets: 1 minute.</summary>
    public static readonly TimeSpan MinimumDrainDuration = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task<DrainStatus> GetDrainAsync(CancellationToken ct)
    {
        var context = await _dataContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var disposal = context.ConfigureAwait(false);
        var drain = await DrainStore
            .ReadAsync(context, Clock.GetUtcNow(), ct)
            .ConfigureAwait(false);
        var (items, total) = await DrainStore
            .BusyAsync(context, DrainIgnoredTrainNames(), ct)
            .ConfigureAwait(false);
        return new DrainStatus(
            Drained: drain is not null,
            drain is null ? null : DateTime.SpecifyKind(drain.Until, DateTimeKind.Utc),
            drain is null ? null : DateTime.SpecifyKind(drain.StartedAt, DateTimeKind.Utc),
            drain?.StartedBy,
            items
                .Select(i => new DrainBusyWork(i.Kind, i.Id, i.Name, i.State, i.Since, i.Host))
                .ToList(),
            total
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> DrainAsync(
        TimeSpan? duration,
        string startedBy,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(startedBy);
        var length = duration ?? DefaultDrainDuration;
        if (length < MinimumDrainDuration || length > TraxDrain.MaximumTimeout)
            return new OperationResult(
                Success: false,
                Message: "Not drained: a drain lasts from 1 minute to 24 hours."
            );
        if (string.IsNullOrWhiteSpace(startedBy) || startedBy.Length > 256)
            return new OperationResult(
                Success: false,
                Message: "Not drained: who set it must be named in at most 256 characters."
            );

        var now = Clock.GetUtcNow();
        var until = now + length;
        var context = await _dataContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
            await DrainStore.SetAsync(context, until, now, startedBy, ct).ConfigureAwait(false);
        _changeSignal?.Notify(ChangeDomain.SchedulerConfig);
        _logger?.LogInformation(
            "{StartedBy} drained every host until {Until:u}",
            startedBy,
            until.UtcDateTime
        );
        return new OperationResult(
            Success: true,
            Id: Trax.Effect.Models.Drain.Drain.SingletonId,
            Count: 1,
            Message: $"Drained until {until.UtcDateTime:u}: no host dispatches, queues a scheduled run or "
                + "claims a job until then or until it is resumed. Running work carries on."
        );
    }

    /// <inheritdoc />
    public async Task<OperationResult> EndDrainAsync(CancellationToken ct)
    {
        var context = await _dataContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        bool held;
        await using (context.ConfigureAwait(false))
            held = await DrainStore
                .ClearAsync(context, Clock.GetUtcNow(), ct)
                .ConfigureAwait(false);
        _changeSignal?.Notify(ChangeDomain.SchedulerConfig);
        if (held)
            _logger?.LogInformation("The drain was ended; every host takes work again");
        return new OperationResult(
            Success: true,
            Id: Trax.Effect.Models.Drain.Drain.SingletonId,
            Count: held ? 1 : 0,
            Message: held
                ? "Resumed: every host takes work again."
                : "No drain was in force; nothing changed."
        );
    }

    // The same list the deploy's drain reads (DrainCommand.IgnoredTrains): the scheduler's own trains only. A train
    // left out of MaxActiveJobs is still cut off when its host is replaced, so it is busy here too.
    private IReadOnlyCollection<string> DrainIgnoredTrainNames() =>
        (_services?.GetServices<DrainIgnoredTrains>() ?? [])
            .SelectMany(t => t.Names)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
