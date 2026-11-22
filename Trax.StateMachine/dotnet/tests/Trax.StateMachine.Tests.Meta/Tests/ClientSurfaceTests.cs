using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Trax.Core.Extensions;

namespace Trax.StateMachine.Tests.Meta.Tests;

/// <summary>
/// The C# client against the table in <c>conformance/client/PROTOCOL.md</c> ("The client surface"), which names each
/// member of the three clients side by side. Every C# name the table gives must be public in the client's assemblies,
/// and every public member of a type the table covers must be in it, so a member added here, or in another client, is
/// a row the pull request has to write. The TypeScript and Rust clients run the same check
/// (<c>ts/src/clientSurface.test.ts</c>, <c>rust/client/tests/client_surface.rs</c>). Enforces
/// docs/adr/0004-the-machine-clients-share-one-surface-table.md.
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0004-the-machine-clients-share-one-surface-table.md")]
public partial class ClientSurfaceTests
{
    private const string Adr = "docs/adr/0004-the-machine-clients-share-one-surface-table.md";

    private const string CSharp = "C#";

    private const string TypesHeader = "| Type | TypeScript | C# | Rust |";
    private const string MembersHeader = "| Member | TypeScript | C# | Rust |";
    private const string Separator = "| --- | --- | --- | --- |";

    private static readonly string[] Extra =
    [
        "## The client surface",
        "| Type | TypeScript | C# | Rust | Swift |",
        "| --- | --- | --- | --- | --- |",
        "| The controller | `A` | `B` | `C` | `D` |",
        "",
        "| Member | TypeScript | C# | Rust | Swift |",
        "| --- | --- | --- | --- | --- |",
        "| Send | `A.send` | `B.SendAsync` | `C.send` | — not yet |",
    ];

    private static readonly Assembly[] ClientAssemblies =
    [
        typeof(Trax.StateMachine.Client.ServerMachineController).Assembly,
        typeof(Trax.Client.TraxClient).Assembly,
    ];

    private sealed record Named(int Line, string Name);

    private sealed record Table(List<string> Languages, List<Named> Types, List<Named> Members);

    [Test]
    public async Task EveryCSharpNameTheTableGives_IsPublic()
    {
        var table = await ReadTable().ConfigureAwait(false);

        var missing = table
            .Types.Concat(table.Members)
            .Where(n =>
                n.Name.Split('.') switch
                {
                    [var type] => FindType(type) is null,
                    [var type, var member] => Members(type)?.Contains(member) != true,
                    _ => true,
                }
            )
            .Select(n =>
                string.Create(CultureInfo.InvariantCulture, $"PROTOCOL.md:{n.Line}: `{n.Name}`")
            )
            .ToList();

        missing
            .Should()
            .BeEmpty(
                "the client surface table names C# members that Trax.StateMachine.Client and Trax.Client do not "
                    + $"make public ({Adr}). Fix the row, or the client:\n  "
                    + string.Join("\n  ", missing)
            );
    }

    [Test]
    public async Task EveryPublicMemberOfACoveredType_IsInTheTable()
    {
        var table = await ReadTable().ConfigureAwait(false);
        var named = table.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var type in table.Types)
        {
            var members =
                Members(type.Name)
                ?? throw new AssertionException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PROTOCOL.md:{type.Line}: `{type.Name}` is not a public type of the client's assemblies."
                    )
                );
            missing.AddRange(
                members
                    .Select(m => $"{type.Name}.{m}")
                    .Where(n => !named.Contains(n))
                    .Order(StringComparer.Ordinal)
            );
        }

        missing
            .Should()
            .BeEmpty(
                "every public member of a type the client surface table covers has a row in "
                    + "conformance/client/PROTOCOL.md, with its TypeScript and Rust names, or `—` and the reason a "
                    + $"language has none ({Adr}). Missing:\n  "
                    + string.Join("\n  ", missing)
            );
    }

    [Test]
    public void Parse_refuses_a_cell_that_is_not_one_name_or_a_reason()
    {
        var act = () =>
            Parse(
                [
                    "## The client surface",
                    TypesHeader,
                    Separator,
                    "| The controller | `A` | `B` | `C` |",
                    "",
                    MembersHeader,
                    Separator,
                    "| Send | `A.send` | `B.SendAsync`, `B.Send` | `C.send` |",
                ],
                CSharp
            );

        act.Should().Throw<AssertionException>().WithMessage("*PROTOCOL.md:8*");
    }

    [Test]
    public void Parse_refuses_a_dash_whose_reason_is_only_a_name()
    {
        var act = () =>
            Parse(
                [
                    "## The client surface",
                    TypesHeader,
                    Separator,
                    "| The controller | `A` | `B` | `C` |",
                    "",
                    MembersHeader,
                    Separator,
                    "| Connect | `A.connect` | — `Options.Connect` | `C.connect` |",
                ],
                CSharp
            );

        act.Should().Throw<AssertionException>().WithMessage("*PROTOCOL.md:8*without the `—`*");
    }

    [Test]
    public void Parse_refuses_a_dash_without_a_reason()
    {
        var act = () =>
            Parse(
                [
                    "## The client surface",
                    TypesHeader,
                    Separator,
                    "| The controller | `A` | — | `C` |",
                    "",
                    MembersHeader,
                    Separator,
                ],
                CSharp
            );

        act.Should().Throw<AssertionException>().WithMessage("*PROTOCOL.md:4*");
    }

    [Test]
    public void Parse_finds_its_own_column_in_a_table_with_another_language()
    {
        var table = Parse(Extra, CSharp);

        table.Languages.Should().Equal("TypeScript", "C#", "Rust", "Swift");
        table.Types.Should().Equal(new Named(4, "B"));
        table.Members.Should().Equal(new Named(8, "B.SendAsync"));
        Parse(Extra, "Rust").Members.Should().Equal(new Named(8, "C.send"));
    }

    [Test]
    public void Parse_refuses_a_table_without_its_column()
    {
        var act = () => Parse(Extra, "Python");

        act.Should()
            .Throw<AssertionException>()
            .WithMessage("PROTOCOL.md:2: the Type table has no Python column*");
    }

    [Test]
    public void Parse_refuses_tables_that_name_different_languages()
    {
        var lines = Extra.ToArray();
        lines[5] = MembersHeader;
        lines[6] = Separator;
        lines[7] = "| Send | `A.send` | `B.SendAsync` | `C.send` |";

        var act = () => Parse(lines, CSharp);

        act.Should()
            .Throw<AssertionException>()
            .WithMessage("PROTOCOL.md:6: the tables name different languages*");
    }

    [Test]
    public void Parse_refuses_a_row_with_a_cell_missing()
    {
        var lines = Extra.ToArray();
        lines[3] = "| The controller | `A` | `B` | `C` |";

        var act = () => Parse(lines, CSharp);

        act.Should().Throw<AssertionException>().WithMessage("PROTOCOL.md:4: a row has 5 cells*");
    }

    // conformance/testees.json has a row per language the kits run, its `language` as the table's header names it.
    // The table has a column for each of those languages and no other.
    [Test]
    public async Task TheTable_HasAColumnForEachTesteeLanguage()
    {
        var path = Path.Combine(RepoRoot.Path, "..", "conformance", "testees.json");
        if (!File.Exists(path))
            Assert.Ignore(
                "conformance/testees.json is absent, so there is no testee list to hold the columns to."
            );

        using var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(path).ConfigureAwait(false)
        );
        var list =
            json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement
            : json.RootElement.ValueKind == JsonValueKind.Object
            && json.RootElement.TryGetProperty("languages", out var testees)
            && testees.ValueKind == JsonValueKind.Array
                ? testees
            : throw new AssertionException("conformance/testees.json has no `languages` list.");
        var languages = list.EnumerateArray()
            .Select(t =>
                t.ValueKind == JsonValueKind.Object
                && t.TryGetProperty("language", out var language)
                && language.ValueKind == JsonValueKind.String
                    ? language.GetString().NotNull()
                    : throw new AssertionException(
                        "conformance/testees.json has a testee without a `language` string."
                    )
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        (await ReadTable().ConfigureAwait(false))
            .Languages.Order(StringComparer.Ordinal)
            .Should()
            .Equal(languages, "the client surface table's languages are the testees'");
    }

    private static async Task<Table> ReadTable()
    {
        var path = Path.Combine(RepoRoot.Path, "..", "conformance", "client", "PROTOCOL.md");
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        return Parse(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), CSharp);
    }

    // The two tables, read strictly: a header row whose first cell is `Type` or `Member` and whose other cells name
    // the languages, a separator, then rows of as many cells. Both tables name the same languages in the same order,
    // and this test reads the column headed with its own. A language's cell is one name in backticks (`Type.Member`
    // or `Name`) or `—` and a reason. Anything else fails, with its line.
    private static Table Parse(string[] lines, string language)
    {
        var start = Array.IndexOf(lines, "## The client surface");
        if (start < 0)
            throw new AssertionException("PROTOCOL.md has no `## The client surface` section.");
        var end = Array.FindIndex(
            lines,
            start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal)
        );
        if (end < 0)
            end = lines.Length;

        List<Named>? types = null;
        List<Named>? members = null;
        string[]? languages = null;
        var i = start + 1;
        while (i < end)
        {
            var header = lines[i];
            if (!header.StartsWith('|'))
            {
                i++;
                continue;
            }
            var headerCells = Cells(header, i + 1, count: null);
            var first = headerCells[0];
            var columns = headerCells[1..];
            var isTypes = string.Equals(first, "Type", StringComparison.Ordinal);
            RequireKnownTable(header, i, first, columns, isTypes);
            RequireConsistent(i, first, isTypes ? types : members, languages, columns);
            languages = columns;
            var column = Array.IndexOf(columns, language) + 1;
            if (column == 0)
                throw new AssertionException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PROTOCOL.md:{i + 1}: the {first} table has no {language} column: {header}"
                    )
                );
            RequireSeparator(lines, i, headerCells.Length);
            i += 2;

            var names = ReadNames(lines, ref i, end, column, headerCells.Length);
            if (isTypes)
                types = names;
            else
                members = names;
        }

        return new Table(
            [.. languages ?? []],
            types ?? throw new AssertionException("PROTOCOL.md: no `| Type | ... |` table"),
            members ?? throw new AssertionException("PROTOCOL.md: no `| Member | ... |` table")
        );
    }

    private static void RequireKnownTable(
        string header,
        int i,
        string first,
        string[] columns,
        bool isTypes
    )
    {
        if (
            (!isTypes && !string.Equals(first, "Member", StringComparison.Ordinal))
            || columns.Length == 0
            || columns.Any(c => c.Length == 0)
            || columns.Distinct(StringComparer.Ordinal).Take(columns.Length + 1).Count()
                != columns.Length
        )
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{i + 1}: a table this test does not know: {header}"
                )
            );
    }

    private static void RequireConsistent(
        int i,
        string first,
        List<Named>? table,
        string[]? languages,
        string[] columns
    )
    {
        if (table is not null)
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{i + 1}: a second {first} table"
                )
            );
        if (languages is not null && !languages.SequenceEqual(columns, StringComparer.Ordinal))
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{i + 1}: the tables name different languages: "
                ) + $"{string.Join(", ", languages)} and {string.Join(", ", columns)}"
            );
    }

    private static void RequireSeparator(string[] lines, int i, int cells)
    {
        var separator = "|" + string.Concat(Enumerable.Repeat(" --- |", cells));
        if (
            i + 1 >= lines.Length
            || !string.Equals(lines[i + 1], separator, StringComparison.Ordinal)
        )
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{i + 2}: no separator row"
                )
            );
    }

    // The names one table's rows give in the column, from i until the table ends; i is left after its last row.
    private static List<Named> ReadNames(string[] lines, ref int i, int end, int column, int cells)
    {
        var names = new List<Named>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (; i < end && lines[i].StartsWith('|'); i++)
        {
            var name = NameOf(Cells(lines[i], i + 1, cells)[column], i + 1);
            if (name is null)
                continue;
            if (!seen.Add(name))
                throw new AssertionException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PROTOCOL.md:{i + 1}: `{name}` is named twice in this table"
                    )
                );
            names.Add(new Named(i + 1, name));
        }
        return names;
    }

    private static string[] Cells(string line, int number, int? count)
    {
        if (
            !line.StartsWith("| ", StringComparison.Ordinal)
            || !line.EndsWith(" |", StringComparison.Ordinal)
        )
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{number}: a row starts with `| ` and ends with ` |`"
                )
            );
        var cells = line[2..^2].Split(" | ").Select(c => c.Trim()).ToArray();
        if (count is not null && cells.Length != count)
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{number}: a row has {count} cells, one for what it is and one per language"
                )
            );
        if (cells[0].Length == 0)
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{number}: the first cell says what the row is"
                )
            );
        return cells;
    }

    private static string? NameOf(string cell, int number)
    {
        if (cell.StartsWith("— ", StringComparison.Ordinal))
        {
            var reason = cell[2..].Trim();
            if (reason.Length == 0)
                throw new AssertionException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PROTOCOL.md:{number}: a `—` gives its reason"
                    )
                );
            // A name behind a dash would go unchecked: it is the cell's name, not its reason.
            if (Name.IsMatch(reason))
                throw new AssertionException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"PROTOCOL.md:{number}: `{cell}` gives a name as its reason; write the name without the `—`"
                    )
                );
            return null;
        }
        if (string.Equals(cell, "—", StringComparison.Ordinal))
            throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{number}: a `—` gives its reason"
                )
            );
        var match = Name.Match(cell);
        return match.Success
            ? match.Groups["name"].Value
            : throw new AssertionException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PROTOCOL.md:{number}: `{cell}` is neither one name in backticks (`Type.Member` or `Name`) nor `—` and a reason"
                )
            );
    }

    private static Type? FindType(string name) =>
        ClientAssemblies
            .SelectMany(a => a.GetExportedTypes())
            .SingleOrDefault(t =>
                !t.IsNested && string.Equals(t.Name, name, StringComparison.Ordinal)
            );

    // A type's public members as a caller names them: methods, properties, events and fields, instance and static, its
    // own and inherited ones (an interface's from the interfaces it extends), as the TypeScript check reads a type's;
    // without what every object has, constructors, accessors, operators, nested types and what the compiler writes
    // for a record.
    private static HashSet<string>? Members(string typeName)
    {
        var type = FindType(typeName);
        if (type is null)
            return null;
        const BindingFlags flags =
            BindingFlags.Public
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.FlattenHierarchy;
        return type.GetMembers(flags)
            .Concat(
                type.IsInterface ? type.GetInterfaces().SelectMany(i => i.GetMembers(flags)) : []
            )
            .Where(m =>
                m.DeclaringType != typeof(object)
                && m.MemberType
                    is MemberTypes.Method
                        or MemberTypes.Property
                        or MemberTypes.Event
                        or MemberTypes.Field
                && m is not MethodInfo { IsSpecialName: true }
                && !m.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            )
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
    }

    [GeneratedRegex(
        @"^`(?<name>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)`$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Name { get; }
}
