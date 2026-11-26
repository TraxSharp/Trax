namespace Trax.Effect.Data.Services.Drain;

/// <summary>One piece of work a drain is waiting on.</summary>
/// <param name="Kind"><see cref="Run"/> or <see cref="Instance"/>.</param>
/// <param name="Id">A run's database id, or an instance's machine and id joined by <c>/</c>.</param>
/// <param name="Name">The run's train, or the instance's machine.</param>
/// <param name="State">The run's state, or the instance's state and what it waits on.</param>
/// <param name="Since">When the run started, or when the instance was last written, in UTC.</param>
/// <param name="Host">The host the run is on, when it records one.</param>
internal sealed record DrainBusyItem(
    string Kind,
    string Id,
    string Name,
    string State,
    DateTime Since,
    string? Host
)
{
    /// <summary>The kind of a run that is Pending or InProgress.</summary>
    public const string Run = "run";

    /// <summary>The kind of a machine instance that is not at rest.</summary>
    public const string Instance = "instance";

    /// <summary>The item as one line of a refusal or a log.</summary>
    public override string ToString() =>
        $"{Kind} {Id} {Name} ({State}, since {Since:u}{(Host is null ? "" : $", on {Host}")})";
}
