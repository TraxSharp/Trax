using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Client;

/// <summary>
/// Reads and writes a generated enum by the names its members carry in <see cref="JsonStringEnumMemberNameAttribute"/>.
/// A name the enum does not have, which a newer host may send, is read as a value no member holds rather than failing
/// the whole answer, and that value is written back as the name it was read from. <see cref="TraxEnum.IsUnknown"/> tells
/// one apart.
/// </summary>
/// <remarks>Generated enums are <c>int</c> enums; one of another size is refused when the converter is first used.</remarks>
public sealed class TraxEnumConverter<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum
> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    // How many names an enum lacks it remembers. A host sending more than this is refused rather than grown without end.
    internal const int MaxUnknownNames = 256;

    private static readonly Dictionary<string, TEnum> ByName = new(StringComparer.Ordinal);
    private static readonly Dictionary<TEnum, string> NameOf = [];
    private static readonly Lock Gate = new();
    private static int _next;

    static TraxEnumConverter()
    {
        if (Unsafe.SizeOf<TEnum>() != sizeof(int))
            throw new NotSupportedException($"{typeof(TEnum).Name} is not an int enum.");
        var lowest = 0;
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var value = (TEnum)field.GetValue(null)!;
            var name =
                field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? field.Name;
            ByName[name] = value;
            NameOf[value] = name;
            lowest = Math.Min(lowest, Unsafe.As<TEnum, int>(ref value));
        }
        _next = lowest - 1;
    }

    /// <inheritdoc />
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"A {typeof(TEnum).Name} is written as a string.");
        var name = reader.GetString()!;
        lock (Gate)
        {
            if (ByName.TryGetValue(name, out var value))
                return value;
            if (ByName.Count - Enum.GetValues<TEnum>().Length >= MaxUnknownNames)
                throw new JsonException(
                    $"The host sent more than {MaxUnknownNames} values {typeof(TEnum).Name} does not have."
                );
            var next = _next--;
            var unknown = Unsafe.As<int, TEnum>(ref next);
            ByName[name] = unknown;
            NameOf[unknown] = name;
            return unknown;
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        string? name;
        lock (Gate)
            NameOf.TryGetValue(value, out name);
        writer.WriteStringValue(
            name ?? throw new JsonException($"{value} is not a value of {typeof(TEnum).Name}.")
        );
    }
}

/// <summary>What a generated enum holds when the host sent a value it does not have.</summary>
public static class TraxEnum
{
    /// <summary>
    /// Whether <paramref name="value"/> is none of the enum's members: a name the host sent that this client's enum
    /// lacks, read by <see cref="TraxEnumConverter{TEnum}"/>.
    /// </summary>
    public static bool IsUnknown<TEnum>(TEnum value)
        where TEnum : struct, Enum => !Enum.IsDefined(value);
}
