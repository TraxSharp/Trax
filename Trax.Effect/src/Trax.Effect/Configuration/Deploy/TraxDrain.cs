using System.Globalization;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// Whether this process was started to drain the hosts sharing its database (<c>--trax-drain [timeout]</c>) or to end
/// a drain (<c>--trax-resume</c>), rather than to serve. A deployment runs the new version's image once with one of
/// them, inside the cluster or the ECS network, the way it runs <c>--trax-migrate</c>: the process reaches the
/// database directly and nothing else, so a deploy needs no endpoint into the app.
/// </summary>
/// <remarks>
/// <para>
/// A drain sets the <c>trax.drain</c> row to lapse after the timeout plus <see cref="Margin"/>, so every host stops
/// starting new work: the job dispatcher dispatches nothing, the manifest manager queues no scheduled run, and no
/// local worker claims a job. It then waits for what is busy to finish: runs that are Pending or InProgress (except a
/// Pending run whose job is still in the queue, unclaimed) and machine instances not at rest (one with an effect in
/// flight, or waiting on a run that is busy). When nothing is, the process exits and leaves the drain in place for
/// the deploy to roll the hosts. Past the timeout it ends the drain and refuses, naming what is still busy, unless
/// <see cref="ForceVariable"/> is <see langword="true"/>: then it logs the list and exits as though drained.
/// </para>
/// <para>
/// The process does not migrate the database, whatever <c>TRAX_MIGRATIONS</c> says, and refuses to serve: a host
/// that reaches its start with either argument fails, so one that does not exit through <c>RunTraxAsync</c> never
/// runs its scheduler beside the hosts it drains.
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/trax-drain">SDK reference</seealso>
public static class TraxDrain
{
    /// <summary>The argument that asks the process to drain the hosts, optionally followed by the timeout.</summary>
    public const string Argument = "--trax-drain";

    /// <summary>The argument that asks the process to end a drain and exit.</summary>
    public const string ResumeArgument = "--trax-resume";

    /// <summary>
    /// The environment variable that makes a drain past its timeout exit as though drained, keeping the drain in
    /// place, so the deploy rolls anyway. <see langword="true"/> forces; empty or <see langword="false"/> does not; any other value refuses
    /// to start.
    /// </summary>
    public const string ForceVariable = "TRAX_DRAIN_FORCE";

    /// <summary>What a drain is recorded as started by when a deploy's process sets it.</summary>
    public const string DeployActor = "deploy";

    /// <summary>How long a drain waits for busy work when the argument names no timeout: 10 minutes.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long a drain set by a deploy outlives its timeout: 10 minutes, time for the deploy to roll the hosts. The
    /// deploy ends it once every service has rolled; a deploy that dies leaves one that lapses at the timeout plus
    /// this.
    /// </summary>
    public static readonly TimeSpan Margin = TimeSpan.FromMinutes(10);

    /// <summary>The longest timeout a drain takes: 24 hours.</summary>
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(24);

    private static readonly Lazy<string[]> ProcessArgs = new(Environment.GetCommandLineArgs);

    /// <summary>Whether the process was started with <see cref="Argument"/> or <see cref="ResumeArgument"/>.</summary>
    public static bool Requested => RequestedIn(ProcessArgs.Value);

    /// <summary>Whether <paramref name="args"/> contain <see cref="Argument"/> or <see cref="ResumeArgument"/>.</summary>
    public static bool RequestedIn(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Any(a =>
            string.Equals(a, Argument, StringComparison.Ordinal)
            || a.StartsWith(Argument + "=", StringComparison.Ordinal)
            || string.Equals(a, ResumeArgument, StringComparison.Ordinal)
        );
    }

    /// <summary>Whether <paramref name="args"/> contain <see cref="ResumeArgument"/>.</summary>
    public static bool ResumeRequestedIn(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains(ResumeArgument, StringComparer.Ordinal);
    }

    /// <summary>
    /// The timeout <paramref name="args"/> ask a drain for: the value after <see cref="Argument"/> (as
    /// <c>--trax-drain 00:10:00</c> or <c>--trax-drain=00:10:00</c>), <see cref="DefaultTimeout"/> when it names none,
    /// or null when the arguments do not ask for a drain. Throws <see cref="ArgumentException"/> for a value that is
    /// not a time span from one second to <see cref="MaximumTimeout"/>, or when both <see cref="Argument"/> and
    /// <see cref="ResumeArgument"/> are given.
    /// </summary>
    public static TimeSpan? TimeoutIn(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        for (var i = 0; i < args.Count; i++)
        {
            string? value;
            if (string.Equals(args[i], Argument, StringComparison.Ordinal))
                value =
                    i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        ? args[i + 1]
                        : null;
            else if (args[i].StartsWith(Argument + "=", StringComparison.Ordinal))
                value = args[i][(Argument.Length + 1)..];
            else
                continue;

            if (ResumeRequestedIn(args))
                throw new ArgumentException(
                    $"The process was started with both {Argument} and {ResumeArgument}. Start it with one: "
                        + $"{Argument} stops the hosts taking new work and waits for what is busy, {ResumeArgument} "
                        + "lets them take work again.",
                    nameof(args)
                );
            return value is null ? DefaultTimeout : ParseTimeout(value, nameof(args));
        }
        return null;
    }

    /// <summary>
    /// Reads <see cref="ForceVariable"/>'s value: true for <see langword="true"/>, false for none, an empty value or
    /// <see langword="false"/>, in any case. Throws <see cref="InvalidOperationException"/> for anything else.
    /// </summary>
    public static bool ForceRequested(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (bool.TryParse(value.Trim(), out var force))
            return force;
        throw new InvalidOperationException(
            $"{ForceVariable} is '{value}'. It takes true, which rolls the deploy even when work is still busy at "
                + "the drain's timeout, or false."
        );
    }

    /// <summary>
    /// Reads a drain timeout written as a .NET time span (<c>00:10:00</c>, <c>1.00:00:00</c>), from one second to
    /// <see cref="MaximumTimeout"/>.
    /// </summary>
    public static TimeSpan ParseTimeout(string value, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (
            !TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var timeout)
            || timeout < TimeSpan.FromSeconds(1)
            || timeout > MaximumTimeout
        )
            throw new ArgumentException(
                $"The drain timeout '{value}' is not a time span from 00:00:01 to {MaximumTimeout:c}. Write it as "
                    + "hours, minutes and seconds, as in '00:10:00' for ten minutes.",
                paramName
            );
        return timeout;
    }
}
