using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Trax.StateMachine;

/// <summary>
/// What a write of a snapshot does to its row's timer, set by the engine on a snapshot it produces and by the draft
/// service on one it writes. Never part of the snapshot's wire or its equality.
/// </summary>
/// <param name="Duration">How long the state's timer waits, or null when the state has no timer: the due time is cleared.</param>
/// <param name="Restart">
/// Whether the write enters the state (a transition, an outcome, a fired timer, a created row): the due time is set
/// from now. Otherwise a row already in the state keeps the due time it was given at entry.
/// </param>
internal readonly record struct SnapshotTimer(TimeSpan? Duration, bool Restart)
{
    /// <summary>The state has no timer.</summary>
    public static SnapshotTimer None => new(Duration: null, Restart: false);
}
