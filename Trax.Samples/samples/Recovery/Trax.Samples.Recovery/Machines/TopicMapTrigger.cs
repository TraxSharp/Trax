using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum TopicMapTrigger
{
    /// <summary>Choose the fields to map and go to the range.</summary>
    ChooseFields,

    /// <summary>Back from the range to the fields.</summary>
    Back,

    /// <summary>Choose the years and build the map.</summary>
    Build,

    /// <summary>Stop the build that is running and go back to the range.</summary>
    CancelBuild,

    /// <summary>Build again with the same choices: a new run.</summary>
    Rebuild,

    /// <summary>Change the choices after a build.</summary>
    Edit,

    /// <summary>
    /// Start a new map on this draft: back to the first step with nothing chosen. From Building it cancels the run,
    /// and from a finished build it leaves the build's state, so its review timer is cancelled.
    /// </summary>
    StartOver,
}
