using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>What a caller needs to reach an operation, as its declaration says. The server enforces it either way.</summary>
public sealed record ClientManifestRequirement
{
    /// <summary>Whether anyone may call it, signed in or not (<c>[TraxAllowAnonymous]</c>).</summary>
    public bool Anonymous { get; init; }

    /// <summary>
    /// The role sets, one per attribute that names roles: the caller holds at least one role of every set. Empty when
    /// no role is required.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> RoleSets { get; init; } = [];

    /// <summary>Policies the caller must satisfy, each of them.</summary>
    public IReadOnlyList<string> Policies { get; init; } = [];
}
