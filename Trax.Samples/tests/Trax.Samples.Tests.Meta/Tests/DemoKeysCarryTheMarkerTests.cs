namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every API key a sample or template hands to <c>AddTraxApiKeyAuth</c>, directly or through a
/// resolver, carries the <c>do-not-use-in-production</c> marker. Trax.Api refuses to start a
/// host outside Development when a registered key carries it, so the marker is what turns a
/// published demo key left in a copied sample into a startup failure instead of a live
/// credential. A key without it (<c>alice-key</c>) is invisible to that check.
///
/// <para>Enforces <c>Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md")]
[TestFixture]
public class DemoKeysCarryTheMarkerTests
{
    private const string Marker = "do-not-use-in-production";

    private static readonly Regex ClassDeclaration = new(
        @"\b(?:class|record|struct)\s+(?<name>\w+)",
        RegexOptions.Compiled
    );

    private static readonly Regex ConstString = new(
        @"\bconst\s+string\s+(?<name>\w+)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled
    );

    private static readonly Regex UsingAlias = new(
        @"^\s*using\s+(?<alias>\w+)\s*=\s*[\w.]*?(?<target>\w+)\s*;",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    private static readonly Regex InlineRegistration = new(
        @"\bAddTraxApiKeyAuth\s*\(",
        RegexOptions.Compiled
    );

    private static readonly Regex ResolverRegistration = new(
        @"\bAddTraxApiKeyAuth\s*<\s*(?<resolver>\w+)\s*>",
        RegexOptions.Compiled
    );

    private static readonly Regex AddCall = new(
        @"\.Add\(\s*(?<key>[^,\)]+?)\s*,",
        RegexOptions.Compiled
    );

    private static readonly Regex DictionaryKey = new(
        @"\[\s*(?<key>""[^""]*""|[\w.]+)\s*\]\s*=",
        RegexOptions.Compiled
    );

    [Test]
    public void EveryRegisteredDemoKey_CarriesTheMarker()
    {
        var files = SourceFiles
            .CSharp("samples", "templates")
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .ToList();
        var constants = Constants(files);

        var offenders = new List<string>();
        var inspected = 0;

        foreach (var (file, keyExpression) in KeySites(files, offenders))
        {
            inspected++;
            var value = Resolve(keyExpression, file, constants);
            var rel = RepoRoot.Relative(file.Path);

            if (value is null)
                offenders.Add(
                    $"{rel}: could not resolve the key `{keyExpression}` to a string literal or a "
                        + "const string, so this guard cannot vouch for it."
                );
            else if (!value.Contains(Marker, StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{rel}: key `{keyExpression}` is \"{value}\".");
        }

        inspected
            .Should()
            .BeGreaterThan(
                15,
                "the sixteen demo keys registered across the samples and templates must all be "
                    + "inspected. Fewer means a sample was deleted, in which case lower this floor "
                    + "deliberately, or the registration shape changed and this guard is silently "
                    + "passing"
            );
        offenders
            .Should()
            .BeEmpty(
                $"every demo API key must contain \"{Marker}\", so Trax.Api refuses to start a "
                    + "host outside Development with it registered. See "
                    + "Trax.Docs/adr/0035-demo-credentials-exist-only-in-development.md:\n"
                    + string.Join("\n", offenders)
            );
    }

    private sealed record SourceFile(string Path, string Text)
    {
        public string Code { get; } = SourceText.StripCommentsAndStrings(Text);
    }

    /// <summary>Every const string, keyed by the name of the type that declares it.</summary>
    private static Dictionary<string, Dictionary<string, string>> Constants(
        IEnumerable<SourceFile> files
    )
    {
        var byType = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var types = ClassDeclaration.Matches(file.Code).ToList();
            foreach (Match constant in ConstString.Matches(file.Text))
            {
                var owner = types.LastOrDefault(t => t.Index < constant.Index);
                if (owner is null)
                    continue;
                var name = owner.Groups["name"].Value;
                if (!byType.TryGetValue(name, out var members))
                    byType[name] = members = new Dictionary<string, string>(StringComparer.Ordinal);
                members[constant.Groups["name"].Value] = constant.Groups["value"].Value;
            }
        }
        return byType;
    }

    /// <summary>
    /// The key expression of every <c>keys.Add(key, ...)</c> inside an inline registration, and
    /// every dictionary key in a resolver named by <c>AddTraxApiKeyAuth&lt;TResolver&gt;()</c>.
    /// </summary>
    private static IEnumerable<(SourceFile File, string Key)> KeySites(
        List<SourceFile> files,
        List<string> offenders
    )
    {
        foreach (var file in files)
        {
            foreach (Match registration in InlineRegistration.Matches(file.Code))
            {
                var open = registration.Index + registration.Length - 1;
                var close = MatchingParen(file.Code, open);
                var arguments = file.Text[(open + 1)..close];
                foreach (Match add in AddCall.Matches(arguments))
                    yield return (file, add.Groups["key"].Value.Trim());
            }

            foreach (Match registration in ResolverRegistration.Matches(file.Code))
            {
                var resolver = registration.Groups["resolver"].Value;
                var declaring = files.FirstOrDefault(f =>
                    Regex.IsMatch(f.Code, $@"\bclass\s+{Regex.Escape(resolver)}\b")
                );
                if (declaring is null)
                {
                    offenders.Add(
                        $"{RepoRoot.Relative(file.Path)}: resolver {resolver} is not declared "
                            + "under samples/ or templates/, so its keys cannot be read."
                    );
                    continue;
                }

                foreach (Match key in DictionaryKey.Matches(declaring.Text))
                    yield return (declaring, key.Groups["key"].Value);
            }
        }
    }

    private static string? Resolve(
        string expression,
        SourceFile site,
        Dictionary<string, Dictionary<string, string>> constants
    )
    {
        if (expression.StartsWith('"') && expression.EndsWith('"') && expression.Length >= 2)
            return expression[1..^1];

        var parts = expression.Split('.');
        var member = parts[^1];
        if (parts.Length == 1)
            return constants
                .Values.Select(m => m.GetValueOrDefault(member))
                .FirstOrDefault(v => v is not null);

        var type = parts[^2];
        foreach (Match alias in UsingAlias.Matches(site.Code))
            if (alias.Groups["alias"].Value == type)
                type = alias.Groups["target"].Value;

        return constants.TryGetValue(type, out var members)
            ? members.GetValueOrDefault(member)
            : null;
    }

    private static int MatchingParen(string code, int open)
    {
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '(')
                depth++;
            else if (code[i] == ')' && --depth == 0)
                return i;
        }
        throw new InvalidOperationException($"Unbalanced parenthesis at offset {open}.");
    }
}
