using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>A state that spawns a child machine, as the startup check reads it.</summary>
/// <param name="Machine">The parent machine's id.</param>
/// <param name="State">The spawning state.</param>
/// <param name="ChildMachine">The child machine's class, resolved among the registered machines.</param>
/// <param name="SystemOwned">Whether the parent is system-owned, which its child must be too.</param>
internal sealed record SpawnDeclaration(
    string Machine,
    string State,
    Type ChildMachine,
    bool SystemOwned
);
