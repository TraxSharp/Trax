using System.Diagnostics;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Utilities;

/// <summary>
/// The wait between two polling cycles, read from the live configuration. A background service
/// built its <see cref="PeriodicTimer"/> once, at start, so an interval changed at runtime (through
/// the operations service, or a saved setting another host wrote) did nothing until a restart.
/// This re-reads the interval at least once a second while it waits, so a shorter interval ends
/// the current wait early and a longer one extends it.
/// </summary>
/// <remarks>
/// The wait is never shorter than <see cref="SchedulerConfigLimits.MinTimerInterval"/>, whatever
/// interval it reads. A zero or negative interval would otherwise end every wait at once, and a
/// loop over it would poll the database as fast as it could run and never reach the point where
/// it notices it is being stopped.
/// </remarks>
internal static class PollingDelay
{
    private static readonly TimeSpan Slice = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Waits until <paramref name="interval"/>, re-read as it goes, has elapsed since the call,
    /// and at least <see cref="SchedulerConfigLimits.MinTimerInterval"/>.
    /// Returns false when <paramref name="stoppingToken"/> is cancelled, so a loop can read
    /// <c>while (await PollingDelay.WaitAsync(...))</c> the way it read the timer. A token that
    /// is already cancelled returns false without waiting.
    /// </summary>
    public static async Task<bool> WaitAsync(
        Func<TimeSpan> interval,
        CancellationToken stoppingToken
    )
    {
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (true)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var wanted = interval();
                if (wanted < SchedulerConfigLimits.MinTimerInterval)
                    wanted = SchedulerConfigLimits.MinTimerInterval;

                var remaining = wanted - elapsed.Elapsed;
                if (remaining <= TimeSpan.Zero)
                    return true;

                await Task.Delay(remaining < Slice ? remaining : Slice, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
