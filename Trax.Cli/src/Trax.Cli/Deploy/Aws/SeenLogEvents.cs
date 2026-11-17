namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// The log events a read has printed, so a poll that starts again at the newest time prints none twice. Only an event
/// at that newest time can come back, so only their ids are kept: a read followed for a day holds a handful, not every
/// id it has seen.
/// </summary>
internal sealed class SeenLogEvents
{
    private readonly HashSet<string> _ids = new(StringComparer.Ordinal);
    private long _newest = long.MinValue;

    public int Count => _ids.Count;

    /// <summary>Records the event and returns whether it is new.</summary>
    public bool Add(string id, long timestamp)
    {
        if (timestamp > _newest)
        {
            _newest = timestamp;
            _ids.Clear();
        }
        else if (timestamp < _newest)
            // The next poll starts at the newest time, so an older event is never read again.
            return true;
        return _ids.Add(id);
    }
}
