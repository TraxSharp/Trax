namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The bounded record of a row's recent client requests, which lets a retried request replay the result it was
/// first answered with.
/// </summary>
public static class RequestIdRing
{
    /// <summary>The most client requests one row records.</summary>
    public const int Capacity = 16;

    /// <summary>
    /// The most bytes one row's ring takes, counted as the UTF-8 JSON text written to the <c>recent_requests</c>
    /// column: 128 KiB. Past it the oldest entries are evicted, as past <see cref="Capacity"/>, and the newest request
    /// is always kept. The text is written with relaxed escaping: a quote or backslash inside a recorded result costs
    /// one extra byte, a control character up to five, and a character of the Basic Multilingual Plane its UTF-8 size;
    /// a supplementary-plane character (an emoji) is still written as two <c>\uXXXX</c> escapes, 12 bytes for its 4.
    /// The retry window is about 128 KiB of results: six results of 20 KiB, one or two at
    /// <see cref="SnapshotLimits.MaxSnapshotBytes"/>. SQLite stores this text as it is; Postgres's <c>jsonb</c> stores
    /// the results unescaped, which is no larger, so the count never understates what the column holds.
    /// </summary>
    public const int MaxBytes = 128 * 1024;

    /// <summary>The longest request id a <c>start</c>, <c>discard</c> or event accepts, in characters.</summary>
    public const int MaxRequestIdLength = 200;

    /// <summary>
    /// Whether <paramref name="requestId"/> is usable: not null, empty or whitespace, not too long, with no control
    /// character (C0, DEL or C1) and no unpaired surrogate. The id is stored on the row, and Postgres cannot store a
    /// NUL or an unpaired surrogate in text at all; the other control characters are refused with them, since none
    /// belongs in an id and SQLite would store what Postgres refuses.
    /// </summary>
    internal static bool IsUsable(string? requestId) =>
        !string.IsNullOrWhiteSpace(requestId)
        && requestId.Length <= MaxRequestIdLength
        && IsStorableText(requestId);

    private static bool IsStorableText(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsControl(c))
                return false;
            if (
                char.IsHighSurrogate(c)
                && i + 1 < value.Length
                && char.IsLowSurrogate(value[i + 1])
            )
                i++;
            else if (char.IsSurrogate(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The fingerprint of one request: the lowercase hex SHA-256 of its trigger and its input in canonical JSON. A
    /// null input and an empty object are the same request.
    /// </summary>
    internal static string Fingerprint(string trigger, System.Text.Json.Nodes.JsonNode? input) =>
        SnapshotFingerprint.Of(
            trigger
                + "\n"
                + (
                    input is null || input is System.Text.Json.Nodes.JsonObject { Count: 0 }
                        ? "null"
                        : CanonicalJson.Serialize(input)
                )
        );

    /// <summary>
    /// <paramref name="ring"/> with <paramref name="entry"/> recorded as its newest request: an earlier entry under
    /// the same id is replaced, and the oldest entries are evicted while there are more than <see cref="Capacity"/>,
    /// or while the ring would take more than <see cref="MaxBytes"/>. The newest entry is always kept.
    /// </summary>
    internal static IReadOnlyList<RecordedRequest> With(
        IReadOnlyList<RecordedRequest>? ring,
        RecordedRequest entry
    )
    {
        var next = (ring ?? [])
            .Where(r => !string.Equals(r.RequestId, entry.RequestId, StringComparison.Ordinal))
            .Append(entry)
            .ToList();
        if (next.Count > Capacity)
            next = next.Skip(next.Count - Capacity).ToList();

        // The array's brackets, and a comma between each two entries.
        var sizes = next.Select(SizeOf).ToList();
        var total = 2 + sizes.Sum() + (next.Count - 1);
        var evict = 0;
        while (total > MaxBytes && next.Count - evict > 1)
            total -= sizes[evict++] + 1;
        return evict == 0 ? next : next.Skip(evict).ToList();
    }

    // The bytes one entry takes in the column's text, written exactly as Serialize writes it.
    internal static int SizeOf(RecordedRequest entry) =>
        System.Text.Encoding.UTF8.GetByteCount(
            System.Text.Json.JsonSerializer.Serialize(ToStored(entry), Json)
        );

    private static Stored ToStored(RecordedRequest r) =>
        new(r.RequestId, r.Fingerprint, r.Revision, r.ResultSnapshot, r.Trigger, r.FromState);

    // The ring as the recent_requests column holds it. The result stays a JSON string, never an object, because
    // jsonb reorders keys and a replayed result must be the canonical wire byte for byte.
    private sealed record Stored(
        string RequestId,
        string Fingerprint,
        long Revision,
        string Result,
        string? Trigger,
        string? FromState
    );

    // Written with relaxed escaping, so what is measured is what the column stores: the default encoder writes every
    // quote inside a result as \u0022 and every non-ASCII character as \uXXXX, up to six times the stored size. The
    // relaxed one leaves the Basic Multilingual Plane unescaped but still escapes a supplementary-plane character as
    // a surrogate pair (12 bytes for 4), so the count can overstate the jsonb column, never understate it. The column
    // is never rendered as HTML, so the HTML-sensitive characters need no escape. Reading accepts either form, so a
    // ring written before still reads.
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(
        System.Text.Json.JsonSerializerDefaults.Web
    )
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The ring as the <c>recent_requests</c> column stores it.</summary>
    internal static string Serialize(IReadOnlyList<RecordedRequest> ring) =>
        System.Text.Json.JsonSerializer.Serialize(ring.Select(ToStored).ToList(), Json);

    /// <summary>
    /// The ring a <c>recent_requests</c> column holds, or null when it holds none. A value that cannot be read is
    /// an empty ring: every request against it fires as new, which is what an evicted one does.
    /// </summary>
    internal static IReadOnlyList<RecordedRequest>? Parse(string? column)
    {
        if (column is null)
            return null;
        try
        {
            return (System.Text.Json.JsonSerializer.Deserialize<List<Stored>>(column, Json) ?? [])
                .Where(s => s is { RequestId: not null, Fingerprint: not null, Result: not null })
                .Select(s => new RecordedRequest(s.RequestId, s.Fingerprint, s.Revision, s.Result)
                {
                    Trigger = s.Trigger,
                    FromState = s.FromState,
                })
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}

/// <summary>One client request a row recorded, with the result it was answered with.</summary>
/// <param name="RequestId">The client's idempotency key.</param>
/// <param name="Fingerprint">
/// A fingerprint of the request (its trigger and its input), so the same key with a different request is told apart.
/// </param>
/// <param name="Revision">The revision the request produced, or 0 from a store that records none.</param>
/// <param name="ResultSnapshot">The snapshot the request was answered with, in canonical wire JSON.</param>
public sealed record RecordedRequest(
    string RequestId,
    string Fingerprint,
    long Revision,
    string ResultSnapshot
)
{
    /// <summary>The trigger the request fired.</summary>
    public string? Trigger { get; init; }

    /// <summary>The state the request fired from.</summary>
    public string? FromState { get; init; }
}
