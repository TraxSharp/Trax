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

    /// <summary>How long the runner waits between passes when nothing is owed. Defaults to one second.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(1);
}
