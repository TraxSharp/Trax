using System.Reflection;
using System.Runtime.CompilerServices;

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
public class ClientSurfaceTests
{
    private const string Adr = "docs/adr/0004-the-machine-clients-share-one-surface-table.md";

    private const int CSharp = 2;

    private const string TypesHeader = "| Type | TypeScript | C# | Rust |";
    private const string MembersHeader = "| Member | TypeScript | C# | Rust |";
    private const string Separator = "| --- | --- | --- | --- |";

    private static readonly Regex Name = new(
        @"^`([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)`$",
        RegexOptions.CultureInvariant
    );

    private static readonly Assembly[] ClientAssemblies =
    [
        typeof(Trax.StateMachine.Client.ServerMachineController).Assembly,
        typeof(Trax.Client.TraxClient).Assembly,
    ];

    private sealed record Named(int Line, string Name);

    private sealed record Table(List<Named> Types, List<Named> Members);

    [Test]
    public void EveryCSharpNameTheTableGives_IsPublic()
    {
        var table = ReadTable(CSharp);

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
            .Select(n => $"PROTOCOL.md:{n.Line}: `{n.Name}`")
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
    public void EveryPublicMemberOfACoveredType_IsInTheTable()
    {
        var table = ReadTable(CSharp);
        var named = table.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var type in table.Types)
        {
            var members =
                Members(type.Name)
                ?? throw new AssertionException(
                    $"PROTOCOL.md:{type.Line}: `{type.Name}` is not a public type of the client's assemblies."
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

    private static Table ReadTable(int column)
    {
        var path = Path.Combine(RepoRoot.Path, "..", "conformance", "client", "PROTOCOL.md");
        return Parse(File.ReadAllText(path).Replace("\r\n", "\n").Split('\n'), column);
    }

    // The two tables, read strictly: a header row as written, a separator, then rows of four cells. A language's cell
    // is one name in backticks (`Type.Member` or `Name`) or `—` and a reason. Anything else fails, with its line.
    private static Table Parse(string[] lines, int column)
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
        var i = start + 1;
        while (i < end)
        {
            var header = lines[i];
            if (!header.StartsWith('|'))
            {
                i++;
                continue;
            }
            var isTypes = header == TypesHeader;
            if (!isTypes && header != MembersHeader)
                throw new AssertionException(
                    $"PROTOCOL.md:{i + 1}: a table this test does not know: {header}"
                );
            if ((isTypes ? types : members) is not null)
                throw new AssertionException($"PROTOCOL.md:{i + 1}: a second {header} table");
            if (i + 1 >= lines.Length || lines[i + 1] != Separator)
                throw new AssertionException($"PROTOCOL.md:{i + 2}: no separator row");
            i += 2;

            var names = new List<Named>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (; i < end && lines[i].StartsWith('|'); i++)
            {
                var name = NameOf(Cells(lines[i], i + 1)[column], i + 1);
                if (name is null)
                    continue;
                if (!seen.Add(name))
                    throw new AssertionException(
                        $"PROTOCOL.md:{i + 1}: `{name}` is named twice in this table"
                    );
                names.Add(new Named(i + 1, name));
            }
            if (isTypes)
                types = names;
            else
                members = names;
        }

        return new Table(
            types ?? throw new AssertionException($"PROTOCOL.md: no `{TypesHeader}` table"),
            members ?? throw new AssertionException($"PROTOCOL.md: no `{MembersHeader}` table")
        );
    }

    private static string[] Cells(string line, int number)
    {
        if (
            !line.StartsWith("| ", StringComparison.Ordinal)
            || !line.EndsWith(" |", StringComparison.Ordinal)
        )
            throw new AssertionException(
                $"PROTOCOL.md:{number}: a row starts with `| ` and ends with ` |`"
            );
        var cells = line[2..^2].Split(" | ").Select(c => c.Trim()).ToArray();
        if (cells.Length != 4)
            throw new AssertionException($"PROTOCOL.md:{number}: a row has four cells");
        if (cells[0].Length == 0)
            throw new AssertionException(
                $"PROTOCOL.md:{number}: the first cell says what the row is"
            );
        return cells;
    }

    private static string? NameOf(string cell, int number)
    {
        if (cell.StartsWith("— ", StringComparison.Ordinal))
            return cell[2..].Trim().Length > 0
                ? null
                : throw new AssertionException($"PROTOCOL.md:{number}: a `—` gives its reason");
        if (cell == "—")
            throw new AssertionException($"PROTOCOL.md:{number}: a `—` gives its reason");
        var match = Name.Match(cell);
        return match.Success
            ? match.Groups[1].Value
            : throw new AssertionException(
                $"PROTOCOL.md:{number}: `{cell}` is neither one name in backticks (`Type.Member` or `Name`) nor `—` and a reason"
            );
    }

    private static Type? FindType(string name) =>
        ClientAssemblies
            .SelectMany(a => a.GetExportedTypes())
            .SingleOrDefault(t => !t.IsNested && t.Name == name);

    // A type's public members as a caller names them: methods, properties, events and fields, its own and static ones,
    // without constructors, accessors, operators, nested types and what the compiler writes for a record.
    private static HashSet<string>? Members(string typeName)
    {
        var type = FindType(typeName);
        if (type is null)
            return null;
        return type.GetMembers(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly
            )
            .Where(m =>
                m.MemberType
                    is MemberTypes.Method
                        or MemberTypes.Property
                        or MemberTypes.Event
                        or MemberTypes.Field
            )
            .Where(m => m is not MethodInfo { IsSpecialName: true })
            .Where(m => !m.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
    }
}
