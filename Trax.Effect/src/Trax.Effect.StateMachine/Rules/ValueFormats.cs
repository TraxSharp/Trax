using System.Globalization;
using System.Text.RegularExpressions;

namespace Trax.Effect.StateMachine;

/// <summary>
/// The string formats a field's CLR type gives it, checked the same way in both engines (the TypeScript twin's
/// <c>formats.ts</c> is the other half): every pattern names ASCII digits and letters only, and each value is checked
/// for what a pattern cannot say, such as February 30. A format accepts what System.Text.Json writes for its type
/// and refuses what the type cannot hold; where the reader is looser (a relative URI, a time without seconds) the
/// format is stricter, never looser. Never throws.
/// </summary>
internal static partial class ValueFormats
{
    public const string Uuid = "uuid";
    public const string DateTime = "date-time";
    public const string DateTimeOffset = "date-time-offset";
    public const string Date = "date";
    public const string Time = "time";
    public const string Duration = "duration";
    public const string Uri = "uri";
    public const string Char = "char";

    /// <summary>The format of a value of <paramref name="type"/>, or null when its JSON string has no fixed shape.</summary>
    public static string? Of(Type type) =>
        type == typeof(Guid) ? Uuid
        : type == typeof(System.DateTime) ? DateTime
        : type == typeof(System.DateTimeOffset) ? DateTimeOffset
        : type == typeof(DateOnly) ? Date
        : type == typeof(TimeOnly) ? Time
        : type == typeof(TimeSpan) ? Duration
        : type == typeof(System.Uri) ? Uri
        : type == typeof(char) ? Char
        : null;

    /// <summary>
    /// The names a JSON string of the enum <paramref name="type"/> may hold, as the machine's JSON writes them, or
    /// null for a type that is not an enum.
    /// </summary>
    public static IReadOnlyList<string>? EnumNames(Type type)
    {
        if (!type.IsEnum)
            return null;
        return EnumMembers(type)
            .Select(f =>
                MemberName(f) ?? System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(f.Name)
            )
            .ToList();
    }

    /// <summary>
    /// The names of the enum <paramref name="type"/> that System.Text.Json reads only as written: those its members
    /// are renamed to with <c>[JsonStringEnumMemberName]</c>. Null when it renames none, or is not an enum.
    /// </summary>
    public static IReadOnlyList<string>? ExactEnumNames(Type type)
    {
        if (!type.IsEnum)
            return null;
        var exact = EnumMembers(type).Select(MemberName).OfType<string>().ToList();
        return exact.Count == 0 ? null : exact;
    }

    private static System.Reflection.FieldInfo[] EnumMembers(Type type) =>
        type.GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
        );

    private static string? MemberName(System.Reflection.FieldInfo member) =>
        member
            .GetCustomAttributes(
                typeof(System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute),
                inherit: false
            )
            .OfType<System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute>()
            .FirstOrDefault()
            ?.Name;

    /// <summary>
    /// Whether <paramref name="value"/> is one of <paramref name="names"/>: exactly, for a name in
    /// <paramref name="exact"/>, else ignoring ASCII case only.
    /// </summary>
    public static bool IsOneOf(
        string value,
        IReadOnlyList<string> names,
        IReadOnlyList<string>? exact = null
    )
    {
        foreach (var name in names)
            if (
                exact is not null && exact.Contains(name, StringComparer.Ordinal)
                    ? string.Equals(value, name, StringComparison.Ordinal)
                    : AsciiEqualsIgnoreCase(value, name)
            )
                return true;
        return false;
    }

    private static bool AsciiEqualsIgnoreCase(string a, string b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
            if (AsciiLower(a[i]) != AsciiLower(b[i]))
                return false;
        return true;
    }

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;

    /// <summary>Whether <paramref name="value"/> has <paramref name="format"/>; an unknown format accepts anything.</summary>
    public static bool Matches(string format, string value) =>
        format switch
        {
            Uuid => UuidPattern().IsMatch(value),
            DateTime => IsDateTime(value, zoneless: true),
            DateTimeOffset => IsDateTime(value, zoneless: false),
            Date => DatePattern().Match(value) is { Success: true } m && IsDay(m, 1),
            Time => TimePattern().Match(value) is { Success: true } t && IsClock(t, 1),
            Duration => IsoDuration.TryParse(value, out _),
            Uri => IsPortableUri(value)
                && System.Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _),
            Char => value.Length == 1,
            _ => true,
        };

    // A date-time whose UTC instant is inside years 1 to 9999, which is all a DateTime or DateTimeOffset holds. One
    // with an offset is read as that offset says; one with none is read in the server's zone by a DateTimeOffset,
    // whose offset is not known here, so it is refused on the range's first and last day, where some zone would
    // push it out. A DateTime holds one with no offset as it is (zoneless).
    private static bool IsDateTime(string value, bool zoneless)
    {
        var m = DateTimePattern().Match(value);
        if (!m.Success || !IsDay(m, 1) || !IsClock(m, 4))
            return false;
        var local = MinuteOf(m);
        if (m.Groups[8].Success)
        {
            var hours = Number(m, 9);
            var minutes = Number(m, 10);
            if (minutes >= 60 || hours * 60 + minutes > 14 * 60)
                return false;
            var offset = (hours * 60 + minutes) * (m.Groups[8].Value == "-" ? -1 : 1);
            return local - offset >= 0 && local - offset <= LastMinute;
        }
        return zoneless
            || m.Groups[7].Success
            || (local >= 24 * 60 && local <= LastMinute - 24 * 60);
    }

    // The minute 9999-12-31T23:59 is from 0001-01-01T00:00, the last a DateTime holds.
    private static readonly long LastMinute = (DayOf(9999, 12, 31) * 24 * 60) + (23 * 60) + 59;

    // The minute groups 1 to 5 (a valid year, month, day, hour and minute) name, counted from 0001-01-01T00:00.
    private static long MinuteOf(Match m) =>
        (DayOf(Number(m, 1), Number(m, 2), Number(m, 3)) * 24 * 60)
        + (Number(m, 4) * 60)
        + Number(m, 5);

    // The day a valid date is in the proleptic Gregorian calendar, counted from 0001-01-01.
    private static long DayOf(int year, int month, int day)
    {
        long y = year - 1;
        var days = (365 * y) + (y / 4) - (y / 100) + (y / 400);
        int[] before = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
        var leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
        return days + before[month - 1] + (leap && month > 2 ? 1 : 0) + day - 1;
    }

    // Groups first, first + 1 and first + 2 hold a year, month and day that name a real day of the proleptic
    // Gregorian calendar from year 1.
    private static bool IsDay(Match m, int first)
    {
        var year = Number(m, first);
        var month = Number(m, first + 1);
        var day = Number(m, first + 2);
        if (year < 1 || month < 1 || month > 12 || day < 1)
            return false;
        var leap = year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
        int[] days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        return day <= days[month - 1];
    }

    // Groups first, first + 1 and first + 2 hold an hour, minute and second of one day, with no leap second.
    private static bool IsClock(Match m, int first) =>
        Number(m, first) < 24 && Number(m, first + 1) < 60 && Number(m, first + 2) < 60;

    private static int Number(Match m, int group)
    {
        var n = 0;
        foreach (var c in m.Groups[group].ValueSpan)
            n = n * 10 + (c - '0');
        return n;
    }

    [GeneratedRegex(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex UuidPattern();

    [GeneratedRegex("^([0-9]{4})-([0-9]{2})-([0-9]{2})\\z", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();

    [GeneratedRegex(
        "^([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\\.[0-9]{1,7})?\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex TimePattern();

    [GeneratedRegex(
        "^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\\.[0-9]{1,7})?(?:(Z)|([+-])([0-9]{2}):([0-9]{2}))?\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex DateTimePattern();

    // An absolute URI that System.Uri reads, in a form it reads the same on every platform: a scheme of two or more
    // characters (one letter would be a Windows drive), then an authority and a path, or a path alone; a query and
    // a fragment after either. The authority is a host (dot-separated letter-digit-hyphen labels, a dotted IPv4
    // address or a bracketed IPv6 one) and an optional port of at most 65535, with no user; a file URI's may be
    // empty, and neither it nor a net.pipe one has a port; a file URI's path holds no ':'. A scheme System.Uri parses with rules of its own
    // (KnownSchemes) takes an authority, except mailto, which takes one plain address. Every other character is an
    // RFC 3986 unreserved or reserved one, or a well-formed %HH escape. Stricter than System.Uri, never looser: the
    // twin checks the same grammar, and this engine asks System.Uri as well (as System.Text.Json reads a Uri), so a
    // platform where it reads one differently refuses on the server rather than storing what the type cannot read.
    internal static bool IsPortableUri(string value)
    {
        if (value.Length > MaxUriLength)
            return false;
        var match = SchemePattern().Match(value);
        if (!match.Success)
            return false;
        var scheme = match.Groups[1].Value.ToLowerInvariant();
        var rest = value[match.Length..];
        if (scheme == "mailto")
        {
            var query = rest.IndexOf('?');
            var address = query < 0 ? rest : rest[..query];
            var at = address.IndexOf('@');
            return at > 0
                && MailboxPattern().IsMatch(address[..at])
                && IsHost(address[(at + 1)..])
                && (query < 0 || IsUriText(rest[(query + 1)..]));
        }
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            var end = rest.IndexOfAny(['/', '?', '#'], 2);
            if (end < 0)
                end = rest.Length;
            if (!IsAuthority(rest[2..end], scheme))
                return false;
            // A file URI's path starting "//" would be read as a UNC share, and a ':' in it as a drive.
            if (
                scheme == "file"
                && (
                    rest[end..].StartsWith("//", StringComparison.Ordinal)
                    || rest[end..].Contains(':')
                )
            )
                return false;
            rest = rest[end..];
        }
        else if (KnownSchemes.Contains(scheme))
            return false;
        var hash = rest.IndexOf('#');
        return hash < 0
            ? IsUriText(rest)
            : IsUriText(rest[..hash]) && IsUriText(rest[(hash + 1)..]);
    }

    // The longest string System.Uri reads.
    private const int MaxUriLength = 65519;

    // The schemes System.Uri parses with rules of its own.
    private static readonly HashSet<string> KnownSchemes =
    [
        "http",
        "https",
        "ws",
        "wss",
        "ftp",
        "file",
        "gopher",
        "nntp",
        "news",
        "mailto",
        "uuid",
        "telnet",
        "ldap",
        "net.tcp",
        "net.pipe",
        "vsmacros",
    ];

    private static bool IsAuthority(string authority, string scheme)
    {
        if (scheme == "file")
            return authority.Length == 0 || IsHost(authority);
        if (scheme == "net.pipe")
            return IsHost(authority);
        var colon = authority.StartsWith('[')
            ? authority.IndexOf(':', authority.IndexOf(']') + 1)
            : authority.IndexOf(':');
        if (authority.StartsWith('[') && authority.IndexOf(']') < 0)
            return false;
        var host = colon < 0 ? authority : authority[..colon];
        var port = colon < 0 ? null : authority[(colon + 1)..];
        return IsHost(host)
            && (
                port is null
                || (
                    PortPattern().IsMatch(port)
                    && int.Parse(port, CultureInfo.InvariantCulture) <= 65535
                )
            );
    }

    // A bracketed IPv6 address, a dotted IPv4 address, or letter-digit-hyphen labels whose last is not all digits.
    private static bool IsHost(string host) =>
        host.StartsWith('[') && host.EndsWith(']')
            ? IsIPv6(host[1..^1])
            : Ipv4Pattern().IsMatch(host) || HostPattern().IsMatch(host);

    // An IPv6 address as RFC 4291 writes it: eight groups of one to four hex digits, or fewer around one "::",
    // the last two of which may be a dotted IPv4 address. No zone.
    private static bool IsIPv6(string address)
    {
        var halves = address.Split("::");
        if (halves.Length > 2)
            return false;
        var groups = 0;
        for (var h = 0; h < halves.Length; h++)
        {
            if (halves[h].Length == 0)
                continue;
            var parts = halves[h].Split(':');
            for (var i = 0; i < parts.Length; i++)
            {
                var last = h == halves.Length - 1 && i == parts.Length - 1;
                if (last && Ipv4Pattern().IsMatch(parts[i]))
                    groups += 2;
                else if (HexGroupPattern().IsMatch(parts[i]))
                    groups += 1;
                else
                    return false;
            }
        }
        return halves.Length == 2 ? groups <= 7 : groups == 8;
    }

    // A path, query or fragment: unreserved and reserved characters but "#", and %HH escapes.
    private static bool IsUriText(string text) => UriTextPattern().IsMatch(text);

    [GeneratedRegex("^([A-Za-z][A-Za-z0-9+.\\-]+):", RegexOptions.CultureInvariant)]
    private static partial Regex SchemePattern();

    [GeneratedRegex("^[A-Za-z0-9._+\\-]+\\z", RegexOptions.CultureInvariant)]
    private static partial Regex MailboxPattern();

    [GeneratedRegex(
        "^(?:[A-Za-z0-9](?:[A-Za-z0-9\\-]{0,61}[A-Za-z0-9])?\\.)*(?=[A-Za-z0-9\\-]*[A-Za-z])[A-Za-z0-9](?:[A-Za-z0-9\\-]{0,61}[A-Za-z0-9])?\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex HostPattern();

    [GeneratedRegex("^[0-9]{1,5}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex PortPattern();

    [GeneratedRegex("^[0-9A-Fa-f]{1,4}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HexGroupPattern();

    [GeneratedRegex(
        "^(?:(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])\\.){3}(?:25[0-5]|2[0-4][0-9]|1[0-9][0-9]|[1-9]?[0-9])\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex Ipv4Pattern();

    [GeneratedRegex(
        "^(?:[A-Za-z0-9\\-._~!$&'()*+,;=:@/?]|%[0-9A-Fa-f]{2})*\\z",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex UriTextPattern();
}
