using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>What one send came to. Never an exception.</summary>
public abstract record SendOutcome
{
    private SendOutcome() { }

    /// <summary>The server applied it and stored <see cref="Snapshot"/>.</summary>
    public sealed record Confirmed(ServerSnapshot Snapshot) : SendOutcome;

    /// <summary>The server refused it.</summary>
    public sealed record Refused(MachineProblem Problem) : SendOutcome;

    /// <summary>It did not get through, or the server could not take it yet; <see cref="ServerMachineController.RetryAsync"/> resends it.</summary>
    public sealed record Failed(MachineProblem Problem) : SendOutcome;

    /// <summary>The controller stopped for good before or while it was sent.</summary>
    public sealed record Stopped(MachineError Error) : SendOutcome;
}
