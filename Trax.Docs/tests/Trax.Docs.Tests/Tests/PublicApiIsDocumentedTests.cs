namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every public type and member a Trax package ships is named on a published docs page.
///
/// <para>The failure mode this exists for is a public API nobody can find: it compiles, its
/// package ships, and the only way to learn it exists is to read the source. The rule that a
/// public API change brings its <c>sdk-reference/</c> change is in <c>AGENTS.md</c>; this is what
/// holds a pull request to it.</para>
///
/// <para>The public API is read from each folder's checked-in baselines
/// (<c>tests/*/PublicApi/*.received.txt</c>), which that folder's <c>PublicApiSurfaceTests</c>
/// keeps equal to what it compiles. A type or member counts as documented when its name, as a
/// reader would search for it, is a word on a published page: <c>GetRunGraph</c> as
/// <c>runGraph</c>, <c>ResumeExecutionAsync</c> as <c>ResumeExecution</c>,
/// <c>LeavesStuckRunsAttribute</c> as <c>LeavesStuckRuns</c>, an enum member
/// <c>NotReached</c> as its GraphQL value <c>NOT_REACHED</c>. That proves the name appears, not
/// that the page describing it is right.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0008-documentation-conventions-are-linted.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0008-documentation-conventions-are-linted.md")]
[TestFixture]
public class PublicApiIsDocumentedTests
{
    /// <summary>
    /// The undocumented public API that predates this guard, one <c>Assembly T:Type</c> or
    /// <c>Assembly M:Type.Member</c> per line. New entries must NOT be added: document the API, or
    /// make it internal. An entry whose API is now documented, or gone, fails the build, so the
    /// list only shrinks.
    /// </summary>
    private const string KnownUndocumentedFile =
        "tests/Trax.Docs.Tests/KnownUndocumentedPublicApi.txt";

    // Members every type has, or the compiler writes, which no page should have to name.
    private static readonly HashSet<string> CompilerMembers = new(StringComparer.Ordinal)
    {
        "ToString",
        "Equals",
        "GetHashCode",
        "Deconstruct",
        "EqualityContract",
        "PrintMembers",
        "<Clone>$",
        "Dispose",
        "DisposeAsync",
        "GetEnumerator",
        "CompareTo",
    };

    private static readonly Regex TypeDeclaration = new(
        @"^(?:\[.*\]\s*)*(?:public|protected) (?:(?:static|sealed|abstract|readonly|partial|ref|unsafe|new)\s+)*"
            + @"(?<kind>record struct|record class|record|class|struct|interface|enum|delegate)\s+(?<decl>.*)$",
        RegexOptions.Compiled
    );

    private static readonly Regex LeadingIdentifier = new(
        @"^(?<name>[A-Za-z_]\w*)(?<rest>.*)$",
        RegexOptions.Compiled
    );

    private static readonly Regex LastIdentifier = new(
        @"(?<name>[A-Za-z_]\w*)\s*$",
        RegexOptions.Compiled
    );

    private static readonly Regex Word = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    [Test]
    public void Every_PublicApi_IsNamed_OnAPublishedPage()
    {
        var baselines = Baselines().ToList();
        baselines
            .Should()
            .NotBeEmpty(
                "the guard reads the public API baselines; finding none means it checks nothing"
            );

        var words = PublishedWords();
        var known = KnownUndocumented();

        var undocumented = baselines
            .SelectMany(b => Undocumented(b.Assembly, File.ReadAllText(b.Path), words))
            .ToHashSet(StringComparer.Ordinal);

        var offenders = undocumented
            .Where(k => !known.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
        var stale = known
            .Where(k => !undocumented.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "every public type and member must be named on a published Trax.Docs page, "
                    + "normally its page under sdk-reference/ (AGENTS.md: a public API change brings "
                    + "its sdk-reference change). Document it, or make it internal if no consumer "
                    + "needs it. See Trax.Docs/adr/0008-documentation-conventions-are-linted.md. "
                    + "Undocumented:\n  "
                    + string.Join("\n  ", offenders)
            );

        stale
            .Should()
            .BeEmpty(
                $"an entry in {KnownUndocumentedFile} whose API is now documented, or no longer "
                    + "exists, is stale: delete it. Stale entries:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    [Test]
    public void Parser_ReadsTypesAndMembers_AsTheBaselinePrintsThem()
    {
        const string baseline = """
            namespace Trax.Sample
            {
                public sealed class Widget : System.IEquatable<Trax.Sample.Widget>
                {
                    public const string Kind = "a (b) c";
                    public Widget(string Name) { }
                    public string Name { get; init; }
                    public static readonly System.TimeSpan CacheFor;
                    public event System.EventHandler? Changed;
                    public System.Threading.Tasks.Task<int> GetCountAsync<T>(int x) { }
                    public (int A, string B) Pair() { }
                    public override string ToString() { }
                    public static implicit operator string(Trax.Sample.Widget w) { }
                    public static Trax.Sample.Widget op_Implicit(string s) { }
                    public sealed class Part
                    {
                        public int Size { get; }
                    }
                }
                [System.AttributeUsage(System.AttributeTargets.Class)]
                public sealed class MarksAttribute : System.Attribute { }
                public interface IWidgets
                {
                    System.Threading.Tasks.Task<Trax.Sample.Widget> FindAsync(string id);
                }
                public enum Shade
                {
                    NotReached = 0,
                }
                public delegate void Ping(int at);
            }
            """;

        Api("Trax.Sample", baseline)
            .Should()
            .BeEquivalentTo([
                "T:Widget",
                "M:Widget.Kind",
                "M:Widget.Name",
                "M:Widget.CacheFor",
                "M:Widget.Changed",
                "M:Widget.GetCountAsync",
                "M:Widget.Pair",
                "T:Widget.Part",
                "M:Widget.Part.Size",
                "T:MarksAttribute",
                "T:IWidgets",
                "M:IWidgets.FindAsync",
                "T:Shade",
                "M:Shade.NotReached",
                "T:Ping",
            ]);
    }

    [TestCase("GetRunGraph", "runGraph")]
    [TestCase("ResumeExecutionAsync", "ResumeExecution")]
    [TestCase("LeavesStuckRunsAttribute", "LeavesStuckRuns")]
    [TestCase("NotReached", "NOT_REACHED")]
    [TestCase("ChainHash", "chainHash")]
    public void Name_IsFound_AsAReaderWouldSearchForIt(string name, string onPage) =>
        Names(name).Should().Contain(onPage);

    [Test]
    public void DashboardComponents_And_EntityMappings_FollowTheirDocumentedType()
    {
        const string baseline = """
            namespace Trax.Dashboard.Components.Pages
            {
                [Microsoft.AspNetCore.Components.Route("/trax/x")]
                public class XPage : Trax.Dashboard.Components.Shared.PollingComponentBase
                {
                    public string Undocumented { get; set; }
                }
            }
            namespace Trax.Effect.Data.Models
            {
                public class PersistentCheckpoint : Trax.Effect.Data.Models.BaseModel { }
                public class PersistentNothing : Trax.Effect.Data.Models.BaseModel { }
            }
            """;

        Undocumented(
                "Trax.Dashboard",
                baseline,
                new HashSet<string>(StringComparer.Ordinal) { "Checkpoint" }
            )
            .Should()
            .BeEquivalentTo(["Trax.Dashboard T:PersistentNothing"]);
    }

    /// <summary>
    /// The public API of one baseline that no published page names, as <c>Assembly T:Type</c> and
    /// <c>Assembly M:Type.Member</c> keys.
    /// </summary>
    private static IEnumerable<string> Undocumented(
        string assembly,
        string baseline,
        IReadOnlySet<string> words
    )
    {
        var exemptTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in Entries(assembly, baseline))
        {
            if (entry.IsType)
            {
                // The dashboard's Blazor components (its pages, dialogs, grids and layout) are
                // public because Razor makes them so; nobody calls them, and dashboard.md
                // documents each by what it shows.
                if (
                    assembly == "Trax.Dashboard"
                    && entry.Rest.Contains("Components.", StringComparison.Ordinal)
                )
                {
                    exemptTypes.Add(entry.Path);
                    continue;
                }

                if (entry.Name == "_Imports")
                {
                    exemptTypes.Add(entry.Path);
                    continue;
                }

                // PersistentX is EF Core's mapping of the model X, documented as X.
                if (
                    entry.Name.StartsWith("Persistent", StringComparison.Ordinal)
                    && entry.Name.Length > "Persistent".Length
                    && IsNamed(entry.Name["Persistent".Length..], words)
                )
                    continue;
            }
            else if (exemptTypes.Contains(entry.Owner))
                continue;

            if (exemptTypes.Any(t => entry.Path.StartsWith(t + ".", StringComparison.Ordinal)))
                continue;

            if (!IsNamed(entry.Name, words))
                yield return $"{assembly} {(entry.IsType ? "T" : "M")}:{entry.Path}";
        }
    }

    private static IEnumerable<string> Api(string assembly, string baseline) =>
        Entries(assembly, baseline).Select(e => $"{(e.IsType ? "T" : "M")}:{e.Path}");

    private sealed record Entry(bool IsType, string Owner, string Name, string Rest)
    {
        public string Path => Owner.Length == 0 ? Name : $"{Owner}.{Name}";
    }

    /// <summary>
    /// The types and members of a baseline as PublicApiGenerator prints it: four spaces of indent
    /// per level, one declaration per line.
    /// </summary>
    private static IEnumerable<Entry> Entries(string assembly, string baseline)
    {
        var stack = new List<(int Indent, string Name, string Kind)>();

        foreach (var raw in baseline.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            var text = line.Trim();
            if (
                text.Length == 0
                || text is "{" or "}"
                || text.StartsWith("namespace ", StringComparison.Ordinal)
            )
                continue;

            var indent = line.Length - line.TrimStart().Length;
            while (stack.Count > 0 && stack[^1].Indent >= indent)
                stack.RemoveAt(stack.Count - 1);

            // An attribute on a line of its own belongs to the next declaration.
            if (text.StartsWith('[') && text.EndsWith(']'))
                continue;

            var owner = string.Join(".", stack.Select(s => s.Name));

            var type = TypeDeclaration.Match(text);
            if (type.Success)
            {
                var kind = type.Groups["kind"].Value;
                var decl = type.Groups["decl"].Value;
                if (kind == "delegate")
                {
                    if (NameBeforeParameters(decl) is { } delegateName)
                        yield return new Entry(true, owner, delegateName, "");
                    continue;
                }

                var declared = LeadingIdentifier.Match(decl);
                if (!declared.Success)
                    continue;

                var name = declared.Groups["name"].Value;
                yield return new Entry(true, owner, name, declared.Groups["rest"].Value);
                stack.Add((indent, name, kind));
                continue;
            }

            if (stack.Count == 0)
                continue;

            var member = MemberName(text, stack[^1].Kind, stack[^1].Name);
            if (member is not null)
                yield return new Entry(false, owner, member, "");
        }
    }

    private static string? MemberName(string text, string ownerKind, string ownerName)
    {
        if (ownerKind == "enum")
            return
                LastIdentifier.Match(text.Split('=')[0].TrimEnd(',')).Groups["name"].Value
                    is { Length: > 0 } v
                ? v
                : null;

        text = Regex.Replace(text, @"^(?:\[[^\]]*\]\s*)+", "");

        var isInterface = ownerKind == "interface";
        if (
            !isInterface
            && !text.StartsWith("public ", StringComparison.Ordinal)
            && !text.StartsWith("protected ", StringComparison.Ordinal)
        )
            return null;

        // An override is documented on the member it overrides; an operator and a constructor
        // are documented with their type.
        if (
            text.Contains(" override ", StringComparison.Ordinal)
            || text.Contains(" operator ", StringComparison.Ordinal)
            || text.StartsWith($"public {ownerName}(", StringComparison.Ordinal)
            || text.StartsWith($"protected {ownerName}(", StringComparison.Ordinal)
            || text.Contains("this[", StringComparison.Ordinal)
        )
            return null;

        // The name ends at whichever comes first: a parameter list, an accessor block, a value
        // or the end of the declaration. A value can hold any character, so it is cut first.
        var equals = text.IndexOf(" = ", StringComparison.Ordinal);
        var head = equals >= 0 ? text[..equals] : text;

        var brace = head.IndexOf('{');
        var name =
            NameBeforeParameters(brace >= 0 ? head[..brace] : head)
            ?? LastIdentifier
                .Match((brace >= 0 ? head[..brace] : head).TrimEnd(';', ' '))
                .Groups["name"]
                .Value;

        // PublicApiGenerator prints some conversions by their metadata name, op_Implicit.
        return
            string.IsNullOrEmpty(name)
            || CompilerMembers.Contains(name)
            || name.StartsWith("op_", StringComparison.Ordinal)
            ? null
            : name;
    }

    /// <summary>
    /// The name of a method or delegate: the identifier before its parameter list, which is the
    /// first <c>(</c> outside a type argument list and not opening a tuple type. Null when there is
    /// no parameter list.
    /// </summary>
    private static string? NameBeforeParameters(string declaration)
    {
        var angle = 0;
        for (var i = 0; i < declaration.Length; i++)
        {
            switch (declaration[i])
            {
                case '<':
                    angle++;
                    break;
                case '>':
                    angle--;
                    break;
                case '(' when angle == 0 && (i == 0 || declaration[i - 1] == ' '):
                    // A tuple type: skip to its closing parenthesis.
                    for (var depth = 0; i < declaration.Length; i++)
                    {
                        if (declaration[i] == '(')
                            depth++;
                        else if (declaration[i] == ')' && --depth == 0)
                            break;
                    }
                    break;
                case '(' when angle == 0:
                    var before = StripTypeArguments(declaration[..i]);
                    return LastIdentifier.Match(before).Groups["name"].Value is { Length: > 0 } name
                        ? name
                        : null;
            }
        }

        return null;
    }

    private static string StripTypeArguments(string text)
    {
        if (!text.EndsWith('>'))
            return text;

        var depth = 0;
        for (var i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '>')
                depth++;
            else if (text[i] == '<' && --depth == 0)
                return text[..i];
        }

        return text;
    }

    private static bool IsNamed(string name, IReadOnlySet<string> words) =>
        Names(name).Any(words.Contains);

    /// <summary>The spellings a page may use for <paramref name="name"/>.</summary>
    private static IEnumerable<string> Names(string name)
    {
        IEnumerable<string> Forms(string n) =>
            [
                n,
                char.ToLowerInvariant(n[0]) + n[1..],
                Regex.Replace(n, "(?<=[a-z0-9])(?=[A-Z])", "_").ToUpperInvariant(),
            ];

        var bases = new List<string> { name };
        if (
            name.StartsWith("Get", StringComparison.Ordinal)
            && name.Length > 3
            && char.IsUpper(name[3])
        )
            bases.Add(name[3..]);
        if (name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5)
            bases.AddRange(bases.Select(b => b[..^5]).ToList());
        if (name.EndsWith("Attribute", StringComparison.Ordinal) && name.Length > 9)
            bases.Add(name[..^9]);

        return bases.Where(b => b.Length > 0).SelectMany(Forms).Distinct(StringComparer.Ordinal);
    }

    /// <summary>Every word on a page traxsharp.net publishes.</summary>
    private static HashSet<string> PublishedWords()
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in RepoRoot.MarkdownFiles())
        {
            if (!RepoRoot.IsPublished(RepoRoot.Relative(file).Replace('\\', '/')))
                continue;
            foreach (Match word in Word.Matches(File.ReadAllText(file)))
                words.Add(word.Value);
        }

        return words;
    }

    /// <summary>Each folder's public API baselines, with the assembly each one describes.</summary>
    private static IEnumerable<(string Assembly, string Path)> Baselines()
    {
        var monorepo = Directory.GetParent(RepoRoot.Path)!.FullName;
        foreach (var folder in Directory.EnumerateDirectories(monorepo, "Trax.*"))
        {
            var tests = Path.Combine(folder, "tests");
            if (!Directory.Exists(tests))
                continue;

            foreach (var project in Directory.EnumerateDirectories(tests))
            {
                var publicApi = Path.Combine(project, "PublicApi");
                if (!Directory.Exists(publicApi))
                    continue;

                foreach (var file in Directory.EnumerateFiles(publicApi, "*.received.txt"))
                    yield return (Path.GetFileName(file)[..^".received.txt".Length], file);
            }
        }
    }

    private static HashSet<string> KnownUndocumented() =>
        File.ReadAllLines(RepoRoot.Combine(KnownUndocumentedFile.Split('/')))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
}
