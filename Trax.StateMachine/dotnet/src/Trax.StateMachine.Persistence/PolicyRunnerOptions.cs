using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// How the runner that fires the policies of machines served from features works through what it owes. Set it with
/// <see cref="StateMachineOptions.PolicyRunner"/>.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/add-state-machines">SDK reference</seealso>
public sealed class PolicyRunnerOptions
{
    /// <summary>How many rows one pass reads and fires at a time. Defaults to 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// The most instances one broadcast fires for one transition. The rest are logged as a warning and not fired.
    /// Defaults to 10,000.
    /// </summary>
    public int FanOutCap { get; set; } = 10_000;

    /// <summary>
    /// How long the runner waits between passes when nothing is owed. Defaults to one second. Derivation's warning of
    /// a policy waiting for another's result counts a timer of one second or more as spacing the two, so a longer
    /// interval lets a timer shorter than it fire first with no warning.
    /// </summary>
    public TimeSpan Interval { get; set; } = PolicyLoops.RunnerInterval;

    // The longest wait Task.Delay takes.
    private static readonly TimeSpan LongestInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    // Refuses a value the runner cannot work with: no rows per pass, a cap it cannot read one past, or a wait
    // Task.Delay throws on, which would stop the runner.
    internal void Validate()
    {
        if (BatchSize < 1)
            throw new ArgumentOutOfRangeException(
                nameof(BatchSize),
                BatchSize,
                "PolicyRunnerOptions.BatchSize must be at least 1."
            );
        if (FanOutCap is < 0 or int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(FanOutCap),
                FanOutCap,
                "PolicyRunnerOptions.FanOutCap must be at least 0 and less than int.MaxValue."
            );
        if (Interval <= TimeSpan.Zero || Interval > LongestInterval)
            throw new ArgumentOutOfRangeException(
                nameof(Interval),
                Interval,
                $"PolicyRunnerOptions.Interval must be more than zero and at most {LongestInterval}."
            );
    }
}
