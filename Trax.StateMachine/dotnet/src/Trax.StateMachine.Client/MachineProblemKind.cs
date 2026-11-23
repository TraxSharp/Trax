using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

/// <summary>Whether a problem is the server's refusal or a request that did not get through.</summary>
public enum MachineProblemKind
{
    /// <summary>The server, or the controller on its behalf, refused.</summary>
    Rejected,

    /// <summary>The request did not reach the server, or its answer did not come back.</summary>
    Transport,
}
