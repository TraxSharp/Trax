using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>The total result of <see cref="ISnapshotDraftService.Discard"/>.</summary>
public abstract record DiscardOutcome
{
    /// <summary>The instance is gone, whether this request deleted it or it was already gone.</summary>
    public sealed record Discarded : DiscardOutcome;

    /// <summary>The discard was refused and the instance is unchanged.</summary>
    /// <param name="Code">Why, for example one of <see cref="RefusalCode"/>.</param>
    /// <param name="Message">A human-readable explanation of the refusal.</param>
    public sealed record Rejected(RefusalCode Code, string Message) : DiscardOutcome;

    private DiscardOutcome() { }
}
