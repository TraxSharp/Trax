using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// What a system-owned instance is for: one or more string parts, such as a source and a partition. Two keys are
/// equal when their parts are equal in order. Parts are encoded so no two different keys share an encoding:
/// <c>("a|b", "c")</c> and <c>("a", "b|c")</c> are different keys with different ids.
/// </summary>
public sealed class MachineKey : IEquatable<MachineKey>
{
    // Strict: a string with an unpaired surrogate has no UTF-8 form, and the lenient encoder would replace it
    // with U+FFFD, giving two different keys one encoding.
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private readonly string[] _parts;

    private MachineKey(string[] parts) => _parts = parts;

    /// <summary>The key's parts, in order.</summary>
    public IReadOnlyList<string> Parts => _parts;

    /// <summary>A key of one or more parts. A part may be empty; it may not be null or hold an unpaired surrogate.</summary>
    /// <param name="parts">The parts, in order.</param>
    /// <exception cref="ArgumentException">No parts, a null part, or a part with no UTF-8 encoding.</exception>
    public static MachineKey Of(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Length == 0)
            throw new ArgumentException("A machine key needs at least one part.", nameof(parts));
        foreach (var part in parts)
        {
            if (part is null)
                throw new ArgumentException("A machine key part cannot be null.", nameof(parts));
            try
            {
                StrictUtf8.GetByteCount(part);
            }
            catch (EncoderFallbackException ex)
            {
                throw new ArgumentException(
                    "A machine key part holds an unpaired surrogate and has no UTF-8 form.",
                    nameof(parts),
                    ex
                );
            }
        }
        return new MachineKey((string[])parts.Clone());
    }

    /// <summary>A key of one part.</summary>
    /// <param name="part">The part.</param>
    public static implicit operator MachineKey(string part) => Of(part);

    /// <summary>
    /// The canonical encoding the id is derived from: for each part in order, the decimal count of its UTF-8
    /// bytes, a colon, then the bytes. A reader that knows the format can split it back into the parts, so two
    /// different keys never share an encoding.
    /// </summary>
    internal byte[] Canonical()
    {
        using var buffer = new MemoryStream();
        foreach (var part in _parts)
        {
            var bytes = StrictUtf8.GetBytes(part);
            buffer.Write(
                Encoding.ASCII.GetBytes(
                    bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":"
                )
            );
            buffer.Write(bytes);
        }
        return buffer.ToArray();
    }

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] MachineKey? other) =>
        other is not null && _parts.AsSpan().SequenceEqual(other._parts);

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as MachineKey);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var part in _parts)
            hash.Add(part, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>The parts, each quoted, for logs and messages.</summary>
    public override string ToString() =>
        "(" + string.Join(", ", _parts.Select(p => "\"" + p + "\"")) + ")";
}
