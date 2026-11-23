using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum TopicMapState
{
    /// <summary>The wizard's first step: which fields of the corpus to map.</summary>
    ChoosingFields,

    /// <summary>The second step: which years.</summary>
    ChoosingRange,

    /// <summary>The map is being built: <see cref="IBuildTopicMapTrain"/> runs.</summary>
    Building,

    /// <summary>The build finished; the context points at the pairs it wrote.</summary>
    Built,

    /// <summary>The build failed, or its run was reaped after its host died.</summary>
    BuildFailed,

    /// <summary>
    /// The build's run was cancelled while the draft stayed in Building: it timed out. An operator cannot
    /// cancel it, because a user's draft is read-only to operators.
    /// </summary>
    BuildCancelled,

    /// <summary>
    /// The built map was not reviewed within <see cref="TopicMapMachine.ReviewWindow"/>: the server moved it here by
    /// the review timer. It can be rebuilt, or its range edited.
    /// </summary>
    Expired,
}
