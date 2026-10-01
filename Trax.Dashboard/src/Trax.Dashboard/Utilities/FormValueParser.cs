using System.Globalization;
using System.Reflection;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Reads what an operator typed into a dialog's text field as a value of the field's type. The
/// Run, Queue and Configure Effect dialogs all use it, so a value means the same in each of them
/// whatever culture or time zone the server, or the circuit, runs in: numbers use <c>.</c> for the
/// decimal point and take no thousands separators, and a date or time with no offset is UTC, as
/// the dashboard's header says every timestamp is. A value that does not read as its type is
/// refused with a message saying what was expected; it is never passed on as a string or as
/// something else that happened to parse. Infrastructure for the dashboard's own dialogs; not
/// intended to be called directly.
/// </summary>
internal static class FormValueParser
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private const DateTimeStyles UtcStyles =
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    private static readonly HashSet<Type> Integers =
    [
        typeof(int),
        typeof(long),
        typeof(short),
        typeof(byte),
        typeof(sbyte),
        typeof(uint),
        typeof(ulong),
        typeof(ushort),
    ];

    private static readonly HashSet<Type> Decimals =
    [
        typeof(double),
        typeof(float),
        typeof(decimal),
    ];

    private static readonly HashSet<Type> OtherScalars =
    [
        typeof(string),
        typeof(bool),
        typeof(char),
        typeof(Guid),
        typeof(DateTime),
        typeof(DateTimeOffset),
        typeof(DateOnly),
        typeof(TimeOnly),
        typeof(TimeSpan),
    ];

    /// <summary>
    /// Whether a value of <paramref name="type"/> is typed as text and read by
    /// <see cref="TryParse"/>. Enums are not: the dialogs offer their names in a drop-down.
    /// </summary>
    /// <param name="type">The field's type, with any <see cref="Nullable{T}"/> already removed.</param>
    public static bool IsScalar(Type type) =>
        Integers.Contains(type) || Decimals.Contains(type) || OtherScalars.Contains(type);

    /// <summary>
    /// Reads <paramref name="text"/> as a <paramref name="type"/>, which <see cref="IsScalar"/>
    /// accepts or is an enum. Blank text is not handled here; each dialog decides what a blank
    /// field means.
    /// </summary>
    /// <param name="text">What the operator typed, not blank.</param>
    /// <param name="type">The field's type, with any <see cref="Nullable{T}"/> already removed.</param>
    /// <param name="value">The value read, boxed as <paramref name="type"/>.</param>
    /// <param name="error">Why the text was refused, phrased for the operator.</param>
    /// <returns><see langword="true"/> when the text reads as the type.</returns>
    public static bool TryParse(string text, Type type, out object? value, out string? error)
    {
        value = Parse(text.Trim(), type);
        error = value is null ? $"'{text}' is not {Expected(type)}." : null;
        return value is not null;
    }

    /// <summary>
    /// Writes a value the way <see cref="TryParse"/> reads it back, so a field shown with the
    /// current value and saved unchanged holds the same value.
    /// </summary>
    /// <param name="value">The current value, or <see langword="null"/>.</param>
    public static string Format(object? value) =>
        value switch
        {
            null => "",
            DateTime dt => (
                dt.Kind == DateTimeKind.Unspecified ? dt : dt.ToUniversalTime()
            ).ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", Invariant),
            DateTimeOffset dto => dto.ToUniversalTime()
                .ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", Invariant),
            DateOnly d => d.ToString("yyyy-MM-dd", Invariant),
            TimeOnly t => t.ToString("HH:mm:ss.FFFFFFF", Invariant),
            TimeSpan ts => ts.ToString("c", Invariant),
            double d => d.ToString("R", Invariant),
            float f => f.ToString("R", Invariant),
            IFormattable formattable => formattable.ToString(null, Invariant),
            _ => value.ToString() ?? "",
        };

    /// <summary>
    /// The placeholder a text field of <paramref name="type"/> shows, naming the format
    /// <see cref="TryParse"/> reads.
    /// </summary>
    /// <param name="type">The field's type, with any <see cref="Nullable{T}"/> already removed.</param>
    public static string Placeholder(Type type) =>
        type switch
        {
            _ when type == typeof(string) => "Enter text",
            _ when Integers.Contains(type) => "Enter a whole number",
            _ when Decimals.Contains(type) => "Enter a number, e.g. 1.5",
            _ when type == typeof(Guid) => "Enter GUID",
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) =>
                "yyyy-MM-dd HH:mm:ss (UTC)",
            _ when type == typeof(DateOnly) => "yyyy-MM-dd",
            _ when type == typeof(TimeOnly) => "HH:mm:ss",
            _ when type == typeof(TimeSpan) => "[d.]hh:mm:ss",
            _ when type == typeof(bool) => "true or false",
            _ when IsScalar(type) => $"Enter {type.Name}",
            _ => $"Enter JSON for {type.Name}",
        };

    /// <summary>
    /// Whether a blank field of this property may stand for "no value": a
    /// <see cref="Nullable{T}"/>, or a reference type its declaration marks nullable.
    /// </summary>
    public static bool AcceptsNull(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            return true;
        if (property.PropertyType.IsValueType)
            return false;

        return new NullabilityInfoContext().Create(property).WriteState
            is NullabilityState.Nullable
                or NullabilityState.Unknown;
    }

    private static object? Parse(string s, Type type)
    {
        if (type == typeof(string))
            return s;
        if (type.IsEnum)
            return Enum.GetNames(type).Contains(s) ? Enum.Parse(type, s) : null;

        if (Integers.Contains(type))
            return ParseInteger(s, type);

        if (type == typeof(double))
            return
                double.TryParse(s, NumberStyles.Float, Invariant, out var d) && double.IsFinite(d)
                ? d
                : null;
        if (type == typeof(float))
            return float.TryParse(s, NumberStyles.Float, Invariant, out var f) && float.IsFinite(f)
                ? f
                : null;
        if (type == typeof(decimal))
            return decimal.TryParse(s, NumberStyles.Float, Invariant, out var m) ? m : null;

        if (type == typeof(bool))
            return bool.TryParse(s, out var b) ? b : null;
        if (type == typeof(char))
            return s.Length == 1 ? s[0] : null;
        if (type == typeof(Guid))
            return Guid.TryParse(s, out var g) ? g : null;
        if (type == typeof(DateTime))
            return DateTime.TryParse(s, Invariant, UtcStyles, out var dt) ? dt : null;
        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.TryParse(s, Invariant, UtcStyles, out var dto) ? dto : null;
        if (type == typeof(DateOnly))
            return DateOnly.TryParse(s, Invariant, DateTimeStyles.None, out var date) ? date : null;
        if (type == typeof(TimeOnly))
            return TimeOnly.TryParse(s, Invariant, DateTimeStyles.None, out var time) ? time : null;
        if (type == typeof(TimeSpan))
            return TimeSpan.TryParse(s, Invariant, out var ts) ? ts : null;

        return null;
    }

    private static object? ParseInteger(string s, Type type)
    {
        const NumberStyles styles = NumberStyles.Integer;
        return type switch
        {
            _ when type == typeof(int) => int.TryParse(s, styles, Invariant, out var v) ? v : null,
            _ when type == typeof(long) => long.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ when type == typeof(short) => short.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ when type == typeof(byte) => byte.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ when type == typeof(sbyte) => sbyte.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ when type == typeof(uint) => uint.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ when type == typeof(ulong) => ulong.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ when type == typeof(ushort) => ushort.TryParse(s, styles, Invariant, out var v)
                ? v
                : null,
            _ => null,
        };
    }

    private static string Expected(Type type) =>
        type switch
        {
            _ when type.IsEnum => $"one of {string.Join(", ", Enum.GetNames(type))}",
            _ when Integers.Contains(type) =>
                $"a whole number that fits a {type.Name}, with no thousands separators",
            _ when Decimals.Contains(type) =>
                "a number written with '.' for the decimal point and no thousands separators",
            _ when type == typeof(Guid) => "a GUID",
            _ when type == typeof(DateTime) || type == typeof(DateTimeOffset) =>
                "a date and time such as 2026-09-28 10:00:00 (UTC unless it gives an offset)",
            _ when type == typeof(DateOnly) => "a date such as 2026-09-28",
            _ when type == typeof(TimeOnly) => "a time such as 10:00:00",
            _ when type == typeof(TimeSpan) => "a duration such as 01:30:00 or 2.00:00:00",
            _ when type == typeof(bool) => "true or false",
            _ when type == typeof(char) => "a single character",
            _ => $"a {type.Name}",
        };
}
