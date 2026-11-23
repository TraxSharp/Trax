using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// How a system-owned instance's id is derived, so the same key always names the same instance.
/// </summary>
/// <remarks>
/// Both steps are RFC 9562 name-based UUIDs, version 5 (SHA-1). The machine's namespace is the UUIDv5 of
/// <see cref="Root"/> and the UTF-8 bytes of the machine's name; the instance id is the UUIDv5 of that namespace and
/// the key's canonical encoding (each part as its UTF-8 byte count, a colon, and its bytes). The id therefore
/// changes if the machine is renamed, as every other draft of the machine does. These ids are fixed: changing
/// <see cref="Root"/> or the encoding would orphan every system instance already stored.
/// </remarks>
public static class MachineInstanceId
{
    /// <summary>Trax's root namespace for machine instance ids. Fixed forever.</summary>
    public static readonly Guid Root = new Guid(
        0xd3cd492,
        0x3485,
        0x4ca5,
        0x8f,
        0xdc,
        0xe2,
        0xb,
        0x35,
        0xed,
        0x5a,
        0x85
    ) /* 0d3cd492-3485-4ca5-8fdc-e20b35ed5a85 */
    ;

    /// <summary>The fixed namespace of the machine named <paramref name="machine"/>.</summary>
    /// <param name="machine">The machine's name (<see cref="IMachine.Name"/>).</param>
    public static Guid Namespace(string machine)
    {
        ArgumentException.ThrowIfNullOrEmpty(machine);
        return UuidV5(Root, Encoding.UTF8.GetBytes(machine));
    }

    /// <summary>The id of the system-owned instance of <paramref name="machine"/> for <paramref name="key"/>.</summary>
    /// <param name="machine">The machine's name (<see cref="IMachine.Name"/>).</param>
    /// <param name="key">The instance's key.</param>
    public static Guid For(string machine, MachineKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return UuidV5(Namespace(machine), key.Canonical());
    }

    /// <summary>An RFC 9562 version 5 UUID of <paramref name="name"/> in <paramref name="namespaceId"/>.</summary>
    internal static Guid UuidV5(Guid namespaceId, ReadOnlySpan<byte> name)
    {
        var input = new byte[16 + name.Length];
        namespaceId.TryWriteBytes(input, bigEndian: true, out _);
        name.CopyTo(input.AsSpan(16));

        // SHA-1 is what version 5 is defined over; this is a name derivation, not a security boundary.
        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
