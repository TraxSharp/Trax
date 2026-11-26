using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Services.IDataContextFactory;
using DrainRow = Trax.Effect.Models.Drain.Drain;

namespace Trax.Effect.Data.Services.Drain;

/// <summary>
/// What a process started with <c>--trax-drain</c> or <c>--trax-resume</c> does, through <c>RunTraxAsync</c>, instead
/// of serving.
/// </summary>
internal static class DrainCommand
{
    /// <summary>
    /// How long a drain waits after setting the row before it first reads what is busy. A host that read the row
    /// just before it was set may still be dispatching or claiming; once its cycle commits, the work shows as busy.
    /// </summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    /// <summary>How often a drain reads what is busy while it waits.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(2);

    /// <summary>
    /// <see cref="DrainAsync(IServiceProvider, TimeSpan, TimeSpan, bool, TimeSpan, TimeSpan, CancellationToken)"/>,
    /// holding the drain for <see cref="TraxDrain.Margin"/> once it has finished waiting.
    /// </summary>
    public static Task DrainAsync(
        IServiceProvider services,
        TimeSpan timeout,
        bool force,
        TimeSpan settle,
        TimeSpan poll,
        CancellationToken ct
    ) => DrainAsync(services, timeout, TraxDrain.Margin, force, settle, poll, ct);

    /// <summary>
    /// Sets the drain to lapse after <paramref name="timeout"/> plus <paramref name="hold"/>, then waits for nothing to
    /// be busy. Once nothing is, or with <paramref name="force"/> once the timeout has passed (logging the list), it
    /// sets its drain to lapse <paramref name="hold"/> from then, keeping when and by whom it was started, and returns:
    /// the roll starts now, and the hold is how long the deploy may take over it. Past the timeout without
    /// <paramref name="force"/> it ends the drain and throws, naming what is busy. Throws when its drain is ended or
    /// replaced while it waits, since the hosts may be taking work again. Cancelled, it leaves the drain to lapse.
    /// </summary>
    public static async Task DrainAsync(
        IServiceProvider services,
        TimeSpan timeout,
        TimeSpan hold,
        bool force,
        TimeSpan settle,
        TimeSpan poll,
        CancellationToken ct
    )
    {
        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var logger = Logger(services);
        var factory = services.GetRequiredService<IDataContextProviderFactory>();
        var ignored = IgnoredTrains(services);

        var started = clock.GetUtcNow();
        var deadline = started + timeout;
        var until = deadline + hold;
        var mine = await SetAsync(factory, clock, until, started, ct).ConfigureAwait(false);
        logger?.LogInformation(
            "Drained: no host starts new work until {Until:u}. Waiting up to {Timeout} for what is busy.",
            until,
            timeout
        );

        await Task.Delay(Min(settle, timeout), clock, ct).ConfigureAwait(false);
        while (true)
        {
            await StillMineAsync(factory, clock, mine, ct).ConfigureAwait(false);
            var (items, total) = await WithContextAsync(
                    factory,
                    c => DrainStore.BusyAsync(c, ignored, ct),
                    ct
                )
                .ConfigureAwait(false);
            var now = clock.GetUtcNow();
            if (total == 0 || now >= deadline)
            {
                // A resume or another drain that landed between the look above and the busy read went unseen, and
                // both answers act on the drain, so look again before either.
                await StillMineAsync(factory, clock, mine, ct).ConfigureAwait(false);
                var list = total == 0 ? null : BusyList(items, total);
                if (list is not null && !force)
                    throw await RefusedAsync(factory, timeout, mine.NotNull(), list)
                        .ConfigureAwait(false);
                var held = await HoldAsync(factory, clock, mine.NotNull(), hold, ct)
                    .ConfigureAwait(false);
                Returned(logger, timeout, held, list);
                return;
            }

            logger?.LogInformation(
                "{Total} still busy, {Left} left before the timeout.",
                total,
                deadline - now
            );
            await Task.Delay(Min(poll, deadline - now), clock, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sets the drain, still <paramref name="mine"/>, to lapse <paramref name="hold"/> from now, and returns when that
    /// is. Throws as <see cref="StillMineAsync"/> does when it is no longer its own.
    /// </summary>
    private static async Task<DateTimeOffset> HoldAsync(
        IDataContextProviderFactory factory,
        TimeProvider clock,
        DrainRow mine,
        TimeSpan hold,
        CancellationToken ct
    )
    {
        var until = clock.GetUtcNow() + hold;
        var kept = await WithContextAsync(
                factory,
                c => DrainStore.HoldOwnedAsync(c, mine, until, ct),
                ct
            )
            .ConfigureAwait(false);
        return kept ? until : throw Replaced();
    }

    /// <summary>What a drain logs as it returns: drained, or forced past its timeout with <paramref name="list"/> busy.</summary>
    private static void Returned(
        ILogger? logger,
        TimeSpan timeout,
        DateTimeOffset held,
        string? list
    )
    {
        if (list is null)
            logger?.LogInformation(
                "Nothing is busy; the hosts can be replaced. The drain holds until {Until:u} or the deploy ends it "
                    + "after the roll.",
                held
            );
        else
            logger?.LogWarning(
                "Still busy after {Timeout}, rolling anyway ({Variable}=true); the drain holds until {Until:u} or "
                    + "the deploy ends it after the roll:\n{Busy}",
                timeout,
                TraxDrain.ForceVariable,
                held,
                list
            );
    }

    /// <summary>
    /// Sets the deploy's drain and returns it read back as stored: a provider may keep the time at a coarser
    /// precision than the clock gives, and <see cref="StillMineAsync"/> compares against the stored one.
    /// </summary>
    private static async Task<DrainRow?> SetAsync(
        IDataContextProviderFactory factory,
        TimeProvider clock,
        DateTimeOffset until,
        DateTimeOffset started,
        CancellationToken ct
    )
    {
        await WithContextAsync(
                factory,
                c => DrainStore.SetAsync(c, until, started, TraxDrain.DeployActor, ct),
                ct
            )
            .ConfigureAwait(false);
        return await WithContextAsync(
                factory,
                c => DrainStore.ReadAsync(c, clock.GetUtcNow(), ct),
                ct
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Throws unless the drain in force is still the one this process set: resumed from the console, ended by another
    /// deploy's resume or replaced by another drain, the hosts may already be taking work, and nothing busy would not
    /// mean the roll cuts nothing off. A drain it does not own is left as it is.
    /// </summary>
    private static async Task StillMineAsync(
        IDataContextProviderFactory factory,
        TimeProvider clock,
        DrainRow? mine,
        CancellationToken ct
    )
    {
        var now = await WithContextAsync(
                factory,
                c => DrainStore.ReadAsync(c, clock.GetUtcNow(), ct),
                ct
            )
            .ConfigureAwait(false);
        if (mine is not null && now is not null && DrainStore.SameDrain(now, mine))
            return;
        throw Replaced();
    }

    private static InvalidOperationException Replaced() =>
        new(
            "The drain was ended or replaced while it waited, so the hosts may be taking work again and nothing "
                + "should be replaced. Deploy again to drain afresh."
        );

    /// <summary>
    /// Ends the drain, if it is still <paramref name="mine"/>, once its timeout passed with <paramref name="list"/>
    /// busy, and returns the refusal naming the list.
    /// </summary>
    private static async Task<InvalidOperationException> RefusedAsync(
        IDataContextProviderFactory factory,
        TimeSpan timeout,
        DrainRow mine,
        string list
    )
    {
        // Ended whatever cancelled the wait, so a refused deploy leaves the hosts taking work. A drain someone else set
        // since is theirs to end.
        await WithContextAsync(
                factory,
                c => DrainStore.ClearOwnedAsync(c, mine, CancellationToken.None),
                CancellationToken.None
            )
            .ConfigureAwait(false);
        return new InvalidOperationException(
            $"Still busy after the drain's timeout of {timeout:c}, so the drain was ended and nothing should be "
                + $"replaced. Let it finish, cancel it, or set {TraxDrain.ForceVariable}=true (trax deploy --force) "
                + $"to replace the hosts anyway:\n{list}"
        );
    }

    /// <summary>Ends the drain, so every host takes work again.</summary>
    public static async Task ResumeAsync(IServiceProvider services, CancellationToken ct)
    {
        var clock = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var logger = Logger(services);
        var factory = services.GetRequiredService<IDataContextProviderFactory>();
        var held = await WithContextAsync(
                factory,
                c => DrainStore.ClearAsync(c, clock.GetUtcNow(), ct),
                ct
            )
            .ConfigureAwait(false);
        logger?.LogInformation(
            held
                ? "The drain is ended; every host takes work again."
                : "There was no drain in force; nothing changed."
        );
    }

    /// <summary>The busy items one per line, with how many more there are past the list.</summary>
    internal static string BusyList(IReadOnlyList<DrainBusyItem> items, int total)
    {
        var lines = items.Select(i => $"  {i}").ToList();
        if (total > items.Count)
            lines.Add(
                string.Create(CultureInfo.InvariantCulture, $"  and {total - items.Count} more")
            );
        return string.Join('\n', lines);
    }

    private static ILogger? Logger(IServiceProvider services) =>
        services
            .GetService<ILoggerFactory>()
            ?.CreateLogger(typeof(DrainCommand).FullName ?? nameof(DrainCommand));

    /// <summary>The trains every registration of <see cref="DrainIgnoredTrains"/> names.</summary>
    internal static List<string> IgnoredTrains(IServiceProvider services) =>
        services
            .GetServices<DrainIgnoredTrains>()
            .SelectMany(t => t.Names)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static TimeSpan Min(TimeSpan a, TimeSpan b) =>
        a < b ? (a < TimeSpan.Zero ? TimeSpan.Zero : a) : (b < TimeSpan.Zero ? TimeSpan.Zero : b);

    private static async Task<T> WithContextAsync<T>(
        IDataContextProviderFactory factory,
        Func<DataContext.IDataContext, Task<T>> work,
        CancellationToken ct
    )
    {
        var context = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var disposal = context.ConfigureAwait(false);
        return await work(context).ConfigureAwait(false);
    }

    private static async Task WithContextAsync(
        IDataContextProviderFactory factory,
        Func<DataContext.IDataContext, Task> work,
        CancellationToken ct
    ) =>
        await WithContextAsync(
                factory,
                async c =>
                {
                    await work(c).ConfigureAwait(false);
                    return true;
                },
                ct
            )
            .ConfigureAwait(false);
}
