using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// Emits the canonical wire bytes for a snapshot. The envelope (machine, version, state, context) is in
/// fixed order; the context is canonicalized per RFC 8785 (JCS): object keys sorted by UTF-16 code unit,
/// numbers formatted by the ECMAScript <c>Number::toString</c> algorithm, strings escaped exactly as
/// <c>JSON.stringify</c> does. The point is byte-for-byte identity with the TypeScript twin, whose
/// <c>JSON.stringify</c> IS the ECMAScript algorithm RFC 8785 defers to.
///
/// <para>Do not route this through <see cref="System.Text.Json.Nodes.JsonNode.ToJsonString()"/>: that
/// uppercases <c>\uXXXX</c> hex, escapes non-ASCII, and formats numbers with .NET's rules (<c>1E+21</c>,
/// not <c>1e+21</c>), none of which match <c>JSON.stringify</c>.</para>
/// </summary>
internal static class CanonicalJson
{
    /// <summary>Full canonical wire for a snapshot: fixed envelope order, JCS-canonical context.</summary>
    public static string SerializeSnapshot(
        string machine,
        int version,
        string state,
        JsonNode? context
    )
    {
        var sb = new StringBuilder();
        sb.Append("{\"machine\":");
        AppendString(sb, machine);
        sb.Append(",\"version\":");
        sb.Append(version.ToString(CultureInfo.InvariantCulture));
        sb.Append(",\"state\":");
        AppendString(sb, state);
        sb.Append(",\"context\":");
        AppendValue(sb, context);
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Canonical JSON for an arbitrary node: object keys sorted (ordinal, recursive), ECMAScript number
    /// formatting, JSON.stringify string escaping. Used for deterministic documents like the exported IR.
    /// </summary>
    public static string Serialize(JsonNode? node)
    {
        var sb = new StringBuilder();
        AppendValue(sb, node);
        return sb.ToString();
    }

    private static void AppendValue(StringBuilder sb, JsonNode? node)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                return;
            case JsonObject obj:
                AppendObject(sb, obj);
                return;
            case JsonArray arr:
                AppendArray(sb, arr);
                return;
            default:
                AppendScalar(sb, node);
                return;
        }
    }

    private static void AppendObject(StringBuilder sb, JsonObject obj)
    {
        sb.Append('{');
        var first = true;
        // Keys sorted by UTF-16 code unit (ordinal) == StringComparer.Ordinal == JS default sort.
        foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!first)
                sb.Append(',');
            first = false;
            AppendString(sb, kv.Key);
            sb.Append(':');
            AppendValue(sb, kv.Value);
        }
        sb.Append('}');
    }

    private static void AppendArray(StringBuilder sb, JsonArray arr)
    {
        sb.Append('[');
        var first = true;
        foreach (var item in arr)
        {
            if (!first)
                sb.Append(',');
            first = false;
            AppendValue(sb, item);
        }
        sb.Append(']');
    }

    private static void AppendScalar(StringBuilder sb, JsonNode node)
    {
        switch (node.GetValueKind())
        {
            case System.Text.Json.JsonValueKind.String:
                AppendString(sb, node.GetValue<string>());
                return;
            case System.Text.Json.JsonValueKind.True:
                sb.Append("true");
                return;
            case System.Text.Json.JsonValueKind.False:
                sb.Append("false");
                return;
            case System.Text.Json.JsonValueKind.Null:
                sb.Append("null");
                return;
            case System.Text.Json.JsonValueKind.Number:
                // Normalise every backing (int/long/double/decimal/parsed) through double, matching the
                // JSON-number-is-an-IEEE-double model the TypeScript twin uses, then format per ECMAScript.
                var raw = node.ToJsonString();
                var value = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
                sb.Append(FormatNumber(value));
                return;
            default:
                throw new ArgumentException(
                    $"Unexpected scalar kind {node.GetValueKind()} in canonical JSON."
                );
        }
    }

    /// <summary>
    /// ECMAScript <c>Number::toString</c> (ECMA-262), which RFC 8785 §3.2.2.3 mandates. Derives the
    /// shortest round-trippable significand from .NET, then applies ECMAScript's decimal-vs-exponential
    /// formatting rules (lowercase <c>e</c>, explicit exponent sign, no leading exponent zeros).
    /// </summary>
    internal static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentException("NaN and Infinity are not valid canonical JSON numbers.");
        if (value == 0)
            return "0"; // Covers -0.0: JSON.stringify(-0) === "0".

        var negative = value < 0;
        var abs = Math.Abs(value);

        // .NET Core 3.0+ "R" is the shortest round-trippable representation.
        var r = abs.ToString("R", CultureInfo.InvariantCulture);

        // Split off an exponent, if any.
        var exp = 0;
        var eIdx = r.IndexOfAny(['E', 'e']);
        var mantissa = r;
        if (eIdx >= 0)
        {
            exp = int.Parse(r.AsSpan(eIdx + 1), NumberStyles.Integer, CultureInfo.InvariantCulture);
            mantissa = r[..eIdx];
        }

        // Fold the decimal point into a single digit string.
        var dot = mantissa.IndexOf('.');
        string allDigits;
        int fracLen;
        if (dot >= 0)
        {
            allDigits = mantissa[..dot] + mantissa[(dot + 1)..];
            fracLen = mantissa.Length - dot - 1;
        }
        else
        {
            allDigits = mantissa;
            fracLen = 0;
        }

        // Strip leading zeros (cosmetic) and trailing zeros (they shift the exponent).
        var start = 0;
        while (start < allDigits.Length - 1 && allDigits[start] == '0')
            start++;
        var end = allDigits.Length;
        var trailingZeros = 0;
        while (end - 1 > start && allDigits[end - 1] == '0')
        {
            end--;
            trailingZeros++;
        }

        var sig = allDigits[start..end];
        var k = sig.Length;
        // value = sig × 10^(exp - fracLen + trailingZeros); write as 0.sig × 10^n, so n = that + k.
        var n = exp - fracLen + trailingZeros + k;

        var digits = FormatDigits(sig, k, n);
        return negative ? "-" + digits : digits;
    }

    // The four ECMA-262 cases for a positive significand `sig` (k digits) with point position `n`.
    private static string FormatDigits(string sig, int k, int n)
    {
        if (k <= n && n <= 21)
            return sig + new string('0', n - k); // integer, no dot, no exponent
        if (0 < n && n <= 21)
            return sig[..n] + "." + sig[n..]; // fixed point with a fraction
        if (-6 < n && n <= 0)
            return "0." + new string('0', -n) + sig; // leading "0.000…"

        // Exponential: d[.ddd]e±exp
        var e = n - 1;
        var mant = k == 1 ? sig : sig[..1] + "." + sig[1..];
        return mant
            + "e"
            + (e >= 0 ? "+" : "-")
            + Math.Abs(e).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Escapes a string exactly as <c>JSON.stringify</c>: only <c>"</c>, <c>\</c>, and the C0 controls are
    /// escaped (with the short forms where they exist, else lowercase <c>\u00xx</c>); every other character,
    /// including non-ASCII, is emitted literally.
    /// </summary>
    private static void AppendString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                        sb.Append("\\u")
                            .Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
