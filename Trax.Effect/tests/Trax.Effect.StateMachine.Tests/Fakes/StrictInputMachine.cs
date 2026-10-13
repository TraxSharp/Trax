using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Fakes;

public enum StrictState
{
    Open,
    Done,
}

public enum StrictTrigger
{
    Submit,
    Check,
}

public enum StrictColor
{
    Red,
    DarkGreen,

    [JsonStringEnumMemberName("sky-blue")]
    SkyBlue,
}

/// <summary>A flags enum, which no schema can describe: <c>SchemaFormatTests</c> checks a record holding one is refused.</summary>
[Flags]
public enum StrictFlags
{
    None = 0,
    Bold = 1,
    Italic = 2,
}

/// <summary>
/// A declarative machine whose <c>Submit</c> takes a <see cref="StrictInput"/>: one field of every CLR type the
/// schema gives a domain, a format or a range beyond its JSON type. <c>Check</c> declares the same record but its
/// guard is a delegate, which reads the input in code.
/// </summary>
public static class StrictInputMachine
{
    public sealed record StrictInput
    {
        public StrictColor Color { get; init; }
        public Guid Id { get; init; }
        public DateTime At { get; init; }
        public DateTimeOffset AtOffset { get; init; }
        public DateOnly Day { get; init; }
        public TimeOnly Clock { get; init; }
        public TimeSpan Patience { get; init; }
        public Uri Link { get; init; } = new("https://example.com/");
        public char Initial { get; init; }
        public decimal Amount { get; init; }
        public float Ratio { get; init; }
        public Half Small { get; init; }
        public double Wide { get; init; }
        public Int128 Huge { get; init; }
        public UInt128 HugeUnsigned { get; init; }
        public int Qty { get; init; }
        public int Price { get; init; }

        /// <summary>Computed: System.Text.Json never sets it on read, so the schema has no field for it.</summary>
        public int Total => Qty * Price;
        public StrictColor? MaybeColor { get; init; }
        public List<StrictColor> Palette { get; init; } = [];
        public Dictionary<string, Guid> Owners { get; init; } = [];
        public Dictionary<int, string> ByNumber { get; init; } = [];
        public Dictionary<StrictColor, int> ByColor { get; init; } = [];
        public Dictionary<Guid, int> ById { get; init; } = [];
    }

    public sealed record EmptyContext;

    public static readonly BuiltMachine<StrictState, StrictTrigger> Built = Build();

    public static SnapshotMachine<StrictState, StrictTrigger> Machine => Built.Engine;

    private static BuiltMachine<StrictState, StrictTrigger> Build()
    {
        var m = new MachineBuilder<StrictState, StrictTrigger>();
        m.Id("strict").Version(1).StartsAt(StrictState.Open, () => new JsonObject());

        m.In(StrictState.Open)
            .Context<EmptyContext>()
            .On(StrictTrigger.Submit)
            .WithInput<StrictInput>()
            .To(StrictState.Done);
        m.In(StrictState.Done).Context<EmptyContext>();
        return m.Build();
    }

    /// <summary>The same input record on a declarative machine whose <c>Check</c> guard is a delegate.</summary>
    public static readonly BuiltMachine<StrictState, StrictTrigger> WithDelegateGuard =
        BuildWithDelegateGuard();

    private static BuiltMachine<StrictState, StrictTrigger> BuildWithDelegateGuard()
    {
        var m = new MachineBuilder<StrictState, StrictTrigger>();
        m.Id("strict-delegate").Version(1).StartsAt(StrictState.Open, () => new JsonObject());

        m.In(StrictState.Open)
            .Context<EmptyContext>()
            .On(StrictTrigger.Check)
            .WithInput<StrictInput>()
            .When((_, input) => input?["amount"] is not null)
            .To(StrictState.Done)
            .On(StrictTrigger.Submit)
            .WithInput<StrictInput>()
            .To(StrictState.Done);
        m.In(StrictState.Done).Context<EmptyContext>();
        return m.Build();
    }

    public static Snapshot Open(string machine = "strict") =>
        new()
        {
            Machine = machine,
            Version = 1,
            State = nameof(StrictState.Open),
            Context = new JsonObject(),
        };

    /// <summary>An input every field of which is valid; tests change one thing about it.</summary>
    public static JsonObject ValidInput() =>
        new()
        {
            ["color"] = "darkGreen",
            ["id"] = "6f9619ff-8b86-d011-b42d-00c04fc964ff",
            ["at"] = "2026-10-12T10:00:00",
            ["atOffset"] = "2026-10-12T10:00:00.1234567+02:00",
            ["day"] = "2024-02-29",
            ["clock"] = "23:59:59.5",
            ["patience"] = "PT5M",
            ["link"] = "https://example.com/a?b=c",
            ["initial"] = "x",
            ["amount"] = 12.5,
            ["ratio"] = 0.25,
            ["small"] = 1.5,
            ["wide"] = 1e300,
            ["huge"] = 1,
            ["hugeUnsigned"] = 1,
            ["qty"] = 2,
            ["price"] = 3,
            ["maybeColor"] = null,
            ["palette"] = new JsonArray("red", "sky-blue"),
            ["owners"] = new JsonObject { ["a"] = "00000000-0000-0000-0000-000000000000" },
            ["byNumber"] = new JsonObject { ["1"] = "a" },
            ["byColor"] = new JsonObject { ["red"] = 1 },
            ["byId"] = new JsonObject { ["6f9619ff-8b86-d011-b42d-00c04fc964ff"] = 1 },
        };

    /// <summary><see cref="ValidInput"/> with <paramref name="field"/> set to <paramref name="value"/>.</summary>
    public static JsonObject InputWith(string field, JsonNode? value)
    {
        var input = ValidInput();
        input[field] = value;
        return input;
    }

    /// <summary>
    /// One value of one field. Its verdict is the CLR's (<see cref="ClrReads"/>), unless <paramref name="Stricter"/>
    /// says why both engines refuse a value the CLR type would read.
    /// </summary>
    public sealed record Case(string Name, string Field, JsonNode? Value, string? Stricter = null)
    {
        public override string ToString() => Name;

        /// <summary>The input this case sends: <see cref="ValidInput"/> with its field set to its value.</summary>
        public JsonObject Input() => InputWith(Field, Value?.DeepClone());

        /// <summary>Whether both engines must accept the case: the CLR reads it, and it is not marked stricter.</summary>
        public bool Valid => Stricter is null && ClrReads(Input(), out _);
    }

    /// <summary>
    /// The CLR's verdict on an input: it reads into <see cref="StrictInput"/> through the typed surface's JSON, stored
    /// as the engines store it, and the record it reads writes back an input the machine accepts again.
    /// </summary>
    public static bool ClrReads(JsonObject input, out string? why)
    {
        if (!TypedSurfaceJson.Reads<StrictInput>(input, out var written, out why))
            return false;
        if (
            Machine.Advance(Open(), nameof(StrictTrigger.Submit), written)
            is AdvanceResult.Rejected r
        )
        {
            why =
                $"the record writes back {written!.ToJsonString()}, which is refused: {r.Reason} {r.Detail}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Values each field's CLR type can and cannot hold. Both engines give each the CLR's verdict, or refuse it where
    /// the case says why they are stricter.
    /// </summary>
    public static IReadOnlyList<Case> Cases { get; } =
    [
        // An enum is one of its names as the machine's JSON writes them (camelCase, or its JsonStringEnumMemberName),
        // compared as System.Text.Json reads them: a member's own name ignoring ASCII case, a renamed one exactly.
        new("enum name", "color", "red"),
        new("enum camelCase name", "color", "darkGreen"),
        new("enum PascalCase name", "color", "DarkGreen"),
        new("enum member name", "color", "sky-blue"),
        new("enum C# name of a renamed member", "color", "skyBlue"),
        new("enum renamed member in upper case", "color", "SKY-BLUE"),
        new("enum renamed member in title case", "color", "Sky-Blue"),
        new("list of enums with a renamed one in upper case", "palette", new JsonArray("SKY-BLUE")),
        new("enum unknown name", "color", "purple"),
        new(
            "enum number as text",
            "color",
            "1",
            Stricter: "a number is not one of the enum's names, though the converter reads it"
        ),
        new("enum empty", "color", ""),
        new("nullable enum unknown", "maybeColor", "purple"),
        new("nullable enum name", "maybeColor", "red"),
        new("list of enums with an unknown one", "palette", new JsonArray("red", "purple")),
        // A Guid is its 36-character D form.
        new("uuid upper case", "id", "6F9619FF-8B86-D011-B42D-00C04FC964FF"),
        new("uuid without hyphens", "id", "6f9619ff8b86d011b42d00c04fc964ff"),
        new("uuid in braces", "id", "{6f9619ff-8b86-d011-b42d-00c04fc964ff}"),
        new("uuid not hex", "id", "6f9619ff-8b86-d011-b42d-00c04fc964fg"),
        new("uuid prose", "id", "not-a-guid"),
        new("uuid with Arabic-Indic digits", "id", "\u0666f9619ff-8b86-d011-b42d-00c04fc964ff"),
        new("dictionary of uuids with a bad one", "owners", new JsonObject { ["a"] = "nope" }),
        // A computed property is not a field: an input carrying it would carry what the record never holds.
        new("computed property sent", "total", 1),
        new("computed property sent as what it computes", "total", 6),
        // A dictionary's keys are checked as their CLR key type: an integer's digits, an enum's names, a Guid.
        new("int key", "byNumber", new JsonObject { ["-5"] = "a" }),
        new("int key at int max", "byNumber", new JsonObject { ["2147483647"] = "a" }),
        new("int key past int max", "byNumber", new JsonObject { ["2147483648"] = "a" }),
        new("int key not a number", "byNumber", new JsonObject { ["x"] = "a" }),
        new("int key with a fraction", "byNumber", new JsonObject { ["1.0"] = "a" }),
        new("int key with an exponent", "byNumber", new JsonObject { ["1e3"] = "a" }),
        new(
            "int key with a leading zero",
            "byNumber",
            new JsonObject { ["01"] = "a" },
            Stricter: "the key is not written as System.Text.Json writes it"
        ),
        new("int key with a space", "byNumber", new JsonObject { [" 1"] = "a" }),
        new("enum key", "byColor", new JsonObject { ["DarkGreen"] = 1 }),
        new("enum key renamed", "byColor", new JsonObject { ["sky-blue"] = 1 }),
        new("enum key renamed in upper case", "byColor", new JsonObject { ["SKY-BLUE"] = 1 }),
        new("enum key unknown", "byColor", new JsonObject { ["purple"] = 1 }),
        new("guid key", "byId", new JsonObject { ["6F9619FF-8B86-D011-B42D-00C04FC964FF"] = 1 }),
        new(
            "guid key without hyphens",
            "byId",
            new JsonObject { ["6f9619ff8b86d011b42d00c04fc964ff"] = 1 }
        ),
        // A date-time is the ISO-8601 form System.Text.Json writes: seconds, at most seven fraction digits, and Z
        // or an offset of at most fourteen hours.
        new("date-time Z", "at", "2026-10-12T10:00:00Z"),
        new("date-time fraction", "at", "2026-10-12T10:00:00.123Z"),
        new("date-time offset", "atOffset", "2026-10-12T10:00:00-14:00"),
        new("date-time offset too far", "atOffset", "2026-10-12T10:00:00+14:30"),
        new(
            "date-time without seconds",
            "at",
            "2026-10-12T10:00",
            Stricter: "System.Text.Json writes the seconds"
        ),
        new("date-time with a space", "at", "2026-10-12 10:00:00"),
        new(
            "date-time eight fraction digits",
            "at",
            "2026-10-12T10:00:00.12345678",
            Stricter: "a DateTime holds seven fraction digits"
        ),
        new("date-time February 30", "at", "2026-02-30T10:00:00"),
        new("date-time hour 24", "at", "2026-10-12T24:00:00"),
        new("date-time leap second", "at", "2026-12-31T23:59:60Z"),
        new("date-time year zero", "at", "0000-01-01T00:00:00"),
        new("date-time lower-case t", "at", "2026-10-12t10:00:00"),
        new("date-time full-width digit", "at", "2026-10-1\uFF12T10:00:00"),
        // Its UTC instant is inside years 1 to 9999. A DateTime with no offset is read as it is; a DateTimeOffset
        // with none is read in the server's zone, so it is refused on the range's first and last day.
        new("date-time the least DateTime", "at", "0001-01-01T00:00:00"),
        new("date-time the greatest DateTime", "at", "9999-12-31T23:59:59.9999999"),
        new("date-time offset before year 1 in UTC", "at", "0001-01-01T00:59:59+01:00"),
        new("date-time offset in year 1 in UTC", "at", "0001-01-01T01:00:00+01:00"),
        new("date-time negative offset in year 1", "at", "0001-01-01T00:00:00-01:00"),
        new("date-time offset past year 9999 in UTC", "at", "9999-12-31T23:59:59.9999999-01:00"),
        new("date-time offset in year 9999 in UTC", "at", "9999-12-31T23:59:59.9999999+01:00"),
        new("date-time-offset before year 1 in UTC", "atOffset", "0001-01-01T00:00:00+01:00"),
        new("date-time-offset past year 9999 in UTC", "atOffset", "9999-12-31T23:30:00-00:31"),
        new("date-time-offset the least, in UTC", "atOffset", "0001-01-01T00:00:00Z"),
        new("date-time-offset the least, with its offset", "atOffset", "0001-01-01T00:00:00+00:00"),
        new(
            "date-time-offset with no offset on the first day",
            "atOffset",
            "0001-01-01T23:00:00",
            Stricter: "read in the server's zone, which could put it before year 1"
        ),
        new(
            "date-time-offset with no offset on the last day",
            "atOffset",
            "9999-12-31T00:00:00",
            Stricter: "read in the server's zone, which could put it after year 9999"
        ),
        new("date-time-offset with no offset on the second day", "atOffset", "0001-01-02T00:00:00"),
        new(
            "date-time-offset with no offset on the next-to-last day",
            "atOffset",
            "9999-12-30T23:59:59"
        ),
        // A date is yyyy-MM-dd, a real day.
        new("date leap day", "day", "2024-02-29"),
        new("date leap day of a common year", "day", "2026-02-29"),
        new("date leap day of a century", "day", "1900-02-29"),
        new("date leap day of a 400th year", "day", "2000-02-29"),
        new("date month 13", "day", "2026-13-01"),
        new("date with a time", "day", "2026-10-12T00:00:00"),
        // A time is HH:mm:ss with at most seven fraction digits.
        new("time midnight", "clock", "00:00:00"),
        new("time fraction", "clock", "12:30:15.1234567"),
        new(
            "time without seconds",
            "clock",
            "12:30",
            Stricter: "System.Text.Json writes the seconds"
        ),
        new("time minute 60", "clock", "12:60:00"),
        // A duration is the ISO-8601 subset a timer reads.
        new("duration", "patience", "P1DT2H30M"),
        new("duration fraction", "patience", "PT0.5S"),
        new("duration .NET form", "patience", "00:05:00"),
        new("duration weeks", "patience", "P1W"),
        new("duration negative", "patience", "-PT5M"),
        new("duration Arabic-Indic digit", "patience", "PT\u0665M"),
        new("duration past TimeSpan", "patience", "P999999999D"),
        new("duration TimeSpan.MaxValue", "patience", "P10675199DT2H48M5.4775807S"),
        new("duration one tick past TimeSpan.MaxValue", "patience", "P10675199DT2H48M5.4775808S"),
        // A Uri is absolute, in a form System.Uri reads the same everywhere: a scheme of two or more characters, a
        // host and a port it can parse, RFC 3986 characters only.
        new("uri", "link", "urn:isbn:0451450523"),
        new("uri relative", "link", "/a/b", Stricter: "the format is an absolute URI"),
        new(
            "uri with a space",
            "link",
            "https://example.com/a b",
            Stricter: "not an RFC 3986 character"
        ),
        new("uri unknown scheme only", "link", "ab:"),
        new(
            "uri known scheme with no authority",
            "link",
            "https:",
            Stricter: "System.Uri reads it only as a relative reference"
        ),
        new("uri one-letter scheme", "link", "c:foo"),
        new("uri unclosed IPv6 host", "link", "http://[::1"),
        new("uri IPv6 host and port", "link", "http://[::1]:8080/a"),
        new("uri port past 65535", "link", "http://a:99999/"),
        new("uri port 65535", "link", "http://a:65535/a?b=c#d"),
        new("uri empty host", "link", "http:///a"),
        new("uri host with an empty label", "link", "http://a..b/"),
        new("uri with a user", "link", "http://u@a/", Stricter: "the grammar takes no user"),
        new("uri file", "link", "file:///tmp/a"),
        new("uri file with a drive", "link", "file:///a:x"),
        new("uri mailto", "link", "mailto:a.b@example.com"),
        new(
            "uri mailto with no address",
            "link",
            "mailto:a",
            Stricter: "System.Uri reads it only as a relative reference"
        ),
        new("uri net.pipe with a port", "link", "net.pipe://a:1/"),
        new(
            "uri malformed escape",
            "link",
            "http://a/%zz",
            Stricter: "System.Uri rewrites the escape"
        ),
        new(
            "uri non-ASCII",
            "link",
            "http://a/\u00e9",
            Stricter: "System.Uri rewrites non-ASCII text"
        ),
        // A char is one UTF-16 code unit.
        new("char", "initial", "\u00e9"),
        new("char empty", "initial", ""),
        new("char two letters", "initial", "ab"),
        new("char astral", "initial", "\ud83d\ude00"),
        // Fractional numbers are bounded by their CLR type.
        new("decimal max", "amount", JsonNode.Parse("7.9e28")),
        new("decimal past max", "amount", JsonNode.Parse("1e30")),
        new("decimal past min", "amount", JsonNode.Parse("-1e30")),
        new("float max", "ratio", JsonNode.Parse("3.4e38")),
        new("float max as System.Text.Json writes it", "ratio", JsonNode.Parse("3.4028235E+38")),
        new("float min as System.Text.Json writes it", "ratio", JsonNode.Parse("-3.4028235E+38")),
        new("float just past its written max", "ratio", JsonNode.Parse("3.4028236e38")),
        new("float past max", "ratio", JsonNode.Parse("3.5e38")),
        new("half max", "small", JsonNode.Parse("65504")),
        new(
            "half past max",
            "small",
            JsonNode.Parse("65505"),
            Stricter: "System.Text.Json rounds it down to the Half's maximum"
        ),
        new("double has no extra range", "wide", JsonNode.Parse("-1.7e308")),
        // A stored number is written as JavaScript writes it, with an exponent from 1e21 on, which an Int128 or a
        // UInt128 does not read.
        new("int128 written with an exponent", "huge", JsonNode.Parse("1e38")),
        new("int128 at 1e21", "huge", JsonNode.Parse("1000000000000000000000")),
        new("int128 below -1e21", "huge", JsonNode.Parse("-1e21")),
        new("int128 just under 1e21", "huge", JsonNode.Parse("999999999999999868928")),
        new("int128 just over -1e21", "huge", JsonNode.Parse("-999999999999999868928")),
        new("int128 fraction", "huge", JsonNode.Parse("1.5")),
        new("uint128 at 1e21", "hugeUnsigned", JsonNode.Parse("1e21")),
        new("uint128 just under 1e21", "hugeUnsigned", JsonNode.Parse("999999999999999868928")),
        new("uint128 negative", "hugeUnsigned", JsonNode.Parse("-1")),
    ];
}
