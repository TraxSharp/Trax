using System.Collections.Concurrent;

namespace Trax.Samples.Recovery.Faults;

/// <summary>
/// Crashes one junction of one run, once. The crash cannot live in the train's input: a retry replays
/// its decisions only when the manifest's input is byte-identical between attempts, so the "crash
/// here" switch is kept beside the run instead, keyed by the run id the input already carries.
/// </summary>
/// <remarks>
/// A singleton, so it lives as long as the process. Killing the process would not show a recovery:
/// a killed run is failed by stuck-job recovery much later, or at the next start, not resumed.
/// </remarks>
public sealed class FaultInjector
{
    private readonly ConcurrentDictionary<string, CrashPoint> _armed = new();
    private readonly ConcurrentDictionary<string, CrashPoint> _everyAttempt = new();

    /// <summary>Arms <paramref name="point"/> for the run <paramref name="runId"/>.</summary>
    public void Arm(string runId, CrashPoint point)
    {
        _everyAttempt.TryRemove(runId, out _);
        if (point == CrashPoint.None)
            _armed.TryRemove(runId, out _);
        else
            _armed[runId] = point;
    }

    /// <summary>
    /// Removes whatever is armed for the run. The last step of each train calls it, so a run that
    /// finished without reaching its crash point leaves nothing behind.
    /// </summary>
    public void Disarm(string runId)
    {
        _armed.TryRemove(runId, out _);
        _everyAttempt.TryRemove(runId, out _);
    }

    /// <summary>
    /// Arms <paramref name="point"/> for the run <paramref name="runId"/> on every attempt, not once:
    /// firing it does not disarm it, so every retry crashes there too, until <see cref="Disarm"/>.
    /// </summary>
    public void ArmEveryAttempt(string runId, CrashPoint point)
    {
        _armed.TryRemove(runId, out _);
        _everyAttempt[runId] = point;
    }

    /// <summary>Whether a crash is still armed for the run.</summary>
    public bool IsArmed(string runId) =>
        _armed.ContainsKey(runId) || _everyAttempt.ContainsKey(runId);

    /// <summary>
    /// Fires the crash armed for <paramref name="runId"/> at <paramref name="point"/>, if any, and
    /// disarms it, so the retry gets past this junction. A crash armed for every attempt fires and
    /// stays armed.
    /// </summary>
    public bool TryFire(string runId, CrashPoint point) =>
        (_everyAttempt.TryGetValue(runId, out var every) && every == point)
        || _armed.TryRemove(new KeyValuePair<string, CrashPoint>(runId, point));
}
