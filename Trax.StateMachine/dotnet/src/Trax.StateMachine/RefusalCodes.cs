using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trax.StateMachine;

/// <summary>The wire spelling of a <see cref="RefusalCode"/>, and its parse.</summary>
public static class RefusalCodes
{
    private static readonly RefusalCode[] Members = Enum.GetValues<RefusalCode>();

    private static readonly Dictionary<string, RefusalCode> ByWire = Members.ToDictionary(
        Wire,
        StringComparer.Ordinal
    );

    // The kebab-case spelling the codes had before they were one enum. A request-id ring written by an older node
    // holds results in it, so a replay still reads; nothing writes it any more.
    private static readonly Dictionary<string, RefusalCode> ByLegacy = Members.ToDictionary(
        m => Wire(m).ToLowerInvariant().Replace('_', '-'),
        StringComparer.Ordinal
    );

    /// <summary>Every member, in declaration order.</summary>
    public static IReadOnlyList<RefusalCode> All => Members;

    /// <summary>The code as it travels: upper snake case, the spelling GraphQL gives the enum (<c>GUARD_FAILED</c>).</summary>
    public static string ToWire(this RefusalCode code) => Wire(code);

    /// <summary>
    /// Reads a code from its wire spelling, or from the kebab-case spelling an older node recorded. Anything else is
    /// not a code.
    /// </summary>
    public static bool TryParse(string? text, out RefusalCode code)
    {
        code = default;
        return text is not null
            && (ByWire.TryGetValue(text, out code) || ByLegacy.TryGetValue(text, out code));
    }

    private static string Wire(RefusalCode code)
    {
        var name = code.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
                builder.Append('_');
            builder.Append(char.ToUpperInvariant(name[i]));
        }
        return builder.ToString();
    }
}
