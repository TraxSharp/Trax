using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.Deploy;
using Trax.Effect.Data.Services.IDataContextFactory;

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
    /// Sets the drain to lapse after <paramref name="timeout"/> plus <see cref="TraxDrain.Margin"/>, then waits for
    /// nothing to be busy. Returns once nothing is, leaving the drain in place. Past the timeout it ends the drain and
    /// throws, naming what is busy, or with <paramref name="force"/> logs the list and returns with the drain kept.
    /// Cancelled, it leaves the drain to lapse.
    /// </summary>
    public static async Task DrainAsync(
        IServiceProvider services,
        TimeSpan timeout,
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
        var until = deadline + TraxDrain.Margin;
        await WithContextAsync(
                factory,
                c => DrainStore.SetAsync(c, until, started, TraxDrain.DeployActor, ct),
                ct
            )
            .ConfigureAwait(false);
        logger?.LogInformation(
            "Drained: no host starts new work until {Until:u}. Waiting up to {Timeout} for what is busy.",
            until,
            timeout
        );

        await Task.Delay(Min(settle, timeout), clock, ct).ConfigureAwait(false);
        while (true)
        {
            var (items, total) = await WithContextAsync(
                    factory,
                    c => DrainStore.BusyAsync(c, ignored, ct),
                    ct
                )
                .ConfigureAwait(false);
            if (total == 0)
            {
                logger?.LogInformation("Nothing is busy; the hosts can be replaced.");
                return;
            }

            var now = clock.GetUtcNow();
            if (now >= deadline)
            {
                await PastTimeoutAsync(
                        factory,
                        clock,
                        logger,
                        timeout,
                        until,
                        force,
                        BusyList(items, total)
                    )
                    .ConfigureAwait(false);
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
    /// What a drain does once its timeout passes with work still busy: with <paramref name="force"/>, logs the list and
    /// returns with the drain kept; otherwise ends the drain and throws, naming the list.
    /// </summary>
    private static async Task PastTimeoutAsync(
        IDataContextProviderFactory factory,
        TimeProvider clock,
        ILogger? logger,
        TimeSpan timeout,
        DateTimeOffset until,
        bool force,
        string list
    )
    {
        if (force)
        {
            logger?.LogWarning(
                "Still busy after {Timeout}, rolling anyway ({Variable}=true); the drain stays until "
                    + "{Until:u} or a scheduler host starts:\n{Busy}",
                timeout,
                TraxDrain.ForceVariable,
                until,
                list
            );
            return;
        }

        // Ended whatever cancelled the wait, so a refused deploy leaves the hosts taking work.
        await WithContextAsync(
                factory,
                c => DrainStore.ClearAsync(c, clock.GetUtcNow(), CancellationToken.None),
                CancellationToken.None
            )
            .ConfigureAwait(false);
        throw new InvalidOperationException(
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
