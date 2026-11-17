using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Trax.StateMachine.Features;

/// <summary>The names the formats share, as the schemas spell them, anchored at both ends of the text.</summary>
internal static partial class Names
{
    [GeneratedRegex(
        @"\A[a-z][a-z0-9]*(-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture
    )]
    internal static partial Regex MachineId();

    [GeneratedRegex(@"\A[A-Z][A-Za-z0-9]*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex Member();

    [GeneratedRegex(@"\A[a-z][a-zA-Z0-9]*\z", RegexOptions.CultureInvariant)]
    internal static partial Regex Word();

    [GeneratedRegex(
        @"\A[a-z][a-z0-9]*(-[a-z0-9]+)*/[a-z0-9]+(-[a-z0-9]+)*\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture
    )]
    internal static partial Regex SystemKey();

    [GeneratedRegex(
        @"\A[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*\z",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture
    )]
    internal static partial Regex TrainName();

    [GeneratedRegex(
        @"\A(?<machine>[a-z][a-z0-9]*(?:-[a-z0-9]+)*)\.(?<member>[A-Za-z_][A-Za-z0-9_]*)\z",
        RegexOptions.CultureInvariant
    )]
    internal static partial Regex ViewSource();

    [GeneratedRegex(
        @"(\A|\.)(__proto__|constructor|prototype)(\.|\z)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture
    )]
    private static partial Regex PrototypeName();

    internal static string MachineIdAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 64) is var id && MachineId().IsMatch(id)
            ? id
            : throw at.Fail("is not a machine id: lowercase words joined by single hyphens.");

    internal static string MemberAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 64) is var name && Member().IsMatch(name)
            ? name
            : throw at.Fail("is not a state or trigger name: a capital, then letters and digits.");

    internal static string WordAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 64) is var word && Word().IsMatch(word)
            ? word
            : throw at.Fail("is not a name: a lowercase letter, then letters and digits.");

    internal static string SystemKeyAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 129) is var key && SystemKey().IsMatch(key)
            ? key
            : throw at.Fail("is not a system key: the machine id, a slash and a name.");

    internal static string TrainAt(At at, JsonNode? node) =>
        at.Text(node, maxLength: 512) is var train && TrainName().IsMatch(train)
            ? train
            : throw at.Fail("is not a train's full name.");

    /// <summary>The IR's field name: any non-empty text but a path through a prototype name.</summary>
    internal static string FieldNameAt(At at, JsonNode? node) =>
        at.Text(node, minLength: 1) is var name && !PrototypeName().IsMatch(name)
            ? name
            : throw at.Fail("is not a field name any engine holds.");

    /// <summary>
    /// A phrase: 1 to 200 characters, starting and ending with something that is not whitespace, and on one line.
    /// Whitespace and line breaks are what the schema's <c>\S</c> and <c>.</c> mean in ECMAScript, which is how its
    /// validator reads them.
    /// </summary>
    internal static string PhraseAt(At at, JsonNode? node)
    {
        var phrase = at.Text(node, 1, 200);
        if (IsSpace(phrase[0]) || IsSpace(phrase[^1]))
            throw at.Fail("must not start or end with whitespace.");
        foreach (var c in phrase)
            if (c is '\n' or '\r' or '\u2028' or '\u2029')
                throw at.Fail("must be on one line.");
        return phrase;
    }

    /// <summary>
    /// The phrase as it is compared: case folded and every run of whitespace one space, so two phrases a reader
    /// would take for the same step are the same.
    /// </summary>
    internal static string Normal(string phrase)
    {
        var builder = new StringBuilder(phrase.Length);
        var space = false;
        foreach (var c in phrase.ToLowerInvariant())
        {
            if (IsSpace(c))
            {
                space = true;
                continue;
            }
            if (space && builder.Length > 0)
                builder.Append(' ');
            space = false;
            builder.Append(c);
        }
        return builder.ToString();
    }

    // ECMAScript's WhiteSpace and LineTerminator, which \s matches.
    private static bool IsSpace(char c) =>
        c
            is '\t'
                or '\n'
                or '\v'
                or '\f'
                or '\r'
                or ' '
                or '\u00a0'
                or '\u1680'
                or (>= '\u2000' and <= '\u200a')
                or '\u2028'
                or '\u2029'
                or '\u202f'
                or '\u205f'
                or '\u3000'
                or '\ufeff';
}
