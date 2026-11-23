using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Trax.Core.Utils;

namespace Trax.Core.Decisions;

/// <summary>Reads an enum's members, in order of their values, and the description each carries.</summary>
internal static class EnumMembers<T>
    where T : struct, Enum
{
    /// <summary>
    /// The members from the lowest value to the highest, as signed numbers: <c>-1</c> comes before
    /// <c>0</c>. <see cref="Enum.GetValues{TEnum}"/> orders by unsigned magnitude, which would put
    /// every negative member last.
    /// </summary>
    public static readonly IReadOnlyList<T> Ordered = Enum.GetValues<T>()
        .Distinct()
        .OrderBy(SignedValue)
        .ToList();

    private static decimal SignedValue(T value) =>
        Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))) switch
        {
            TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => Convert.ToInt64(
                value,
                CultureInfo.InvariantCulture
            ),
            _ => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        };

    private static readonly Dictionary<string, T> ByName = Enum.GetNames<T>()
        .ToDictionary(n => n, Enum.Parse<T>, StringComparer.Ordinal);

    public static bool TryParse(string name, out T value) => ByName.TryGetValue(name, out value);

    public static string? Description(T value) =>
        typeof(T)
            .GetField(value.ToString(), BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<DescriptionAttribute>()
            ?.Description;

    public static int IndexOf(T value)
    {
        for (var i = 0; i < Ordered.Count; i++)
            if (EqualityComparer<T>.Default.Equals(Ordered[i], value))
                return i;

        return -1;
    }
}
