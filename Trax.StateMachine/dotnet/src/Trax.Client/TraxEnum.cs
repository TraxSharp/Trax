using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.Client;

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
