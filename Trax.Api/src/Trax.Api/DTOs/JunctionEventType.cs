using Trax.Effect.Services.TrainEventBroadcaster;

namespace Trax.Api.DTOs;

/// <summary>What happened to a step of a run.</summary>
public enum JunctionEventType
{
    /// <summary>A junction started.</summary>
    JunctionStarted,

    /// <summary>A junction returned a result.</summary>
    JunctionCompleted,

    /// <summary>A junction failed.</summary>
    JunctionFailed,

    /// <summary>A junction was stopped by a cancellation the run was asked for.</summary>
    JunctionCancelled,

    /// <summary>A routing step asked a question and the run acts on the answer.</summary>
    Decided,

    /// <summary>A decider's answer the run would not act on; the routing step fails.</summary>
    DecisionRefused,

    /// <summary>A routing step sent the run down a track.</summary>
    Routed,
}
