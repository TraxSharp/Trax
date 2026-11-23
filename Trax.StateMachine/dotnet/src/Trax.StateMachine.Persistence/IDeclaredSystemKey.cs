using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// A system machine that declares the keys of its instances, as one served from features does: one fixed key, or a
/// number of named parts (<c>keyedBy</c>).
/// </summary>
internal interface IDeclaredSystemKey
{
    /// <summary>Why <paramref name="key"/> is not a key of one of the machine's instances, or null when it is.</summary>
    string? KeyProblem(MachineKey key);
}
