using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>A system-owned machine instance, as <see cref="IMachineInstances.Start{TMachine}"/> returns it.</summary>
/// <param name="Id">The instance id, derived from the machine's name and the key.</param>
/// <param name="Machine">The machine's name.</param>
/// <param name="State">The state the instance is in now.</param>
/// <param name="Created">True when this call created the instance, false when it already existed.</param>
public sealed record MachineInstance(Guid Id, string Machine, string State, bool Created);
