using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>Where a controller is.</summary>
public enum MachineStatus
{
    /// <summary>No snapshot from the server yet.</summary>
    Loading,

    /// <summary>Showing what the server confirmed, and any prediction on top of it.</summary>
    Ready,

    /// <summary>Stopped for good; see <see cref="MachineView.Error"/>.</summary>
    Stopped,
}
