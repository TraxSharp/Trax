namespace Trax.Scheduler.Utilities;

/// <summary>
/// The instant a retention or timeout reaches back to. <c>now - span</c> throws when the span
/// reaches past the start of the calendar, so a retention of <see cref="TimeSpan.MaxValue"/>
/// (meaning "keep it for ever") stopped the sweep that computed it, every cycle.
/// </summary>
internal static class TimeCutoff
{
    /// <summary>
    /// <paramref name="now"/> less <paramref name="span"/>, held to the range a
    /// <see cref="DateTime"/> can represent: a span reaching past <see cref="DateTime.MinValue"/>
    /// gives it (nothing is older), and a negative span reaching past
    /// <see cref="DateTime.MaxValue"/> gives that.
    /// </summary>
    public static DateTime Before(DateTime now, TimeSpan span)
    {
        var ticks = (Int128)now.Ticks - span.Ticks;

        if (ticks < DateTime.MinValue.Ticks)
            return DateTime.SpecifyKind(DateTime.MinValue, now.Kind);

        if (ticks > DateTime.MaxValue.Ticks)
            return DateTime.SpecifyKind(DateTime.MaxValue, now.Kind);

        return new DateTime((long)ticks, now.Kind);
    }
}
