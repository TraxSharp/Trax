using Trax.Effect.Models.Metadata;

namespace Trax.Effect.Services.ServiceTrain;

/// <summary>
/// The run going on in the current async flow, so what the run's code does without being handed
/// its row (a log line written through <c>ILogger</c>) can still be tied to it.
/// </summary>
/// <remarks>
/// <c>ServiceTrain.Run</c> sets it in its own frame as soon as it has the run's row, so it holds
/// for the rest of that run and for everything the run awaits, and the caller's value comes back
/// when <c>Run</c> returns. A train run inside a junction sets its own, so its lines name the
/// inner run. A flow outside any run sees null.
/// </remarks>
internal static class CurrentRun
{
    private static readonly AsyncLocal<Metadata?> Run = new();

    /// <summary>The row of the run on this flow, or null outside a run.</summary>
    public static Metadata? Metadata
    {
        get => Run.Value;
        set => Run.Value = value;
    }

    /// <summary>
    /// The id of the run on this flow, or null outside a run or before its row has an id (a host
    /// with no data provider never gives it one).
    /// </summary>
    public static long? MetadataId => Run.Value is { Id: > 0 } metadata ? metadata.Id : null;
}
