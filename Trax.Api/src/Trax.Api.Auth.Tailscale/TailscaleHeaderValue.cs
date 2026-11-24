using System.Globalization;
using System.Text;

namespace Trax.Api.Auth.Tailscale;

/// <summary>
/// Reads a Tailscale identity header. Tailscale serve writes an ASCII value as it is, and one with other characters as
/// RFC 2047 Q-encoded UTF-8 words (<c>=?utf-8?q?Ren=C3=A9?=</c>), several separated by a space when it is long.
/// </summary>
internal static class TailscaleHeaderValue
{
    private const string Prefix = "=?utf-8?q?";
    private const string Suffix = "?=";

    /// <summary>The value decoded, or null when it is encoded and not as Tailscale encodes it.</summary>
    public static string? Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!value.StartsWith("=?", StringComparison.Ordinal))
            return value;
        var bytes = new List<byte>();
        foreach (var word in value.Split(' '))
        {
            if (
                !word.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                || !word.EndsWith(Suffix, StringComparison.Ordinal)
                || word.Length < Prefix.Length + Suffix.Length
            )
                return null;
            var text = word[Prefix.Length..^Suffix.Length];
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '_')
                    bytes.Add((byte)' ');
                else if (c == '=')
                {
                    if (
                        i + 2 >= text.Length
                        || !byte.TryParse(
                            text.AsSpan(i + 1, 2),
                            NumberStyles.AllowHexSpecifier,
                            CultureInfo.InvariantCulture,
                            out var b
                        )
                    )
                        return null;
                    bytes.Add(b);
                    i += 2;
                }
                else if (c is > ' ' and < (char)127)
                    bytes.Add((byte)c);
                else
                    return null;
            }
        }
        try
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true
            ).GetString([.. bytes]);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
