using System.Text.RegularExpressions;
using AwesomeAssertions;
using CsCheck;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Clients;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The clients <c>trax client</c> writes, for a host's manifest and with <c>--operations</c>, over schemas and documents whose names are drawn
/// from the words each language reserves, the names each client's runtime and records use, names that differ only in
/// case, and short random identifiers. For every case the generator either refuses it with a sentence
/// (<see cref="InvalidOperationException"/>) or writes a client that holds up: the C# one compiles against Trax.Client,
/// and every name the TypeScript one declares, imports or exports is an identifier no keyword takes, once per module
/// and once in the index. The hand-written cases in <see cref="OperationsClientGeneratorTests"/> each pin one refusal;
/// this covers the combinations nobody listed.
///
/// <para>A failure prints the seed CsCheck shrank it to. Pin it as its own test with
/// <c>Sample(..., seed: "...")</c> before fixing the code, so the case stays covered.</para>
/// </summary>
public partial class GeneratedNamesPropertyTests
{
    // Names that have broken a generated client before, or would: keywords of either language, the type operators,
    // the lib's generics, the runtimes' own names, the members every C# record or enum has, and casings of one word.
    private static readonly string[] Hostile =
    [
        "Record",
        "Array",
        "Promise",
        "Partial",
        "keyof",
        "readonly",
        "unique",
        "infer",
        "type",
        "interface",
        "class",
        "default",
        "enum",
        "in",
        "is",
        "out",
        "async",
        "await",
        "yield",
        "let",
        "static",
        "eval",
        "arguments",
        "string",
        "number",
        "bigint",
        "never",
        "unknown",
        "object",
        "undefined",
        "symbol",
        "int",
        "bool",
        "namespace",
        "global",
        "value__",
        "constructor",
        "configureTraxOperations",
        "traxTransport",
        "httpTransport",
        "requireSecureEndpoint",
        "send",
        "subscribe",
        "execute",
        "TraxDocument",
        "TraxSink",
        "TraxAnswer",
        "TraxOperationError",
        "TraxOperations",
        "TraxOperationsClient",
        "TraxClient",
        "Optional",
        "Equals",
        "ToString",
        "GetHashCode",
        "EqualityContract",
        "Clone",
        "Deconstruct",
        "GetType",
        "ReferenceEquals",
        "MemberwiseClone",
        "Finalize",
        "PrintMembers",
        "Value",
        "Runs",
        "runs",
        "RUNS",
        "RunsResult",
        "RunsVariables",
        "runsDocument",
        "Foo",
        "foo",
        "FOO",
        "foo_bar",
        "FOO_BAR",
        "fooBar",
        "_",
        "_x",
        "x1",
        "A",
        "a",
    ];

    private static readonly Gen<string> Random = Gen.Select(
        Gen.Char["abAB_"],
        Gen.Char["abAB_09"].Array[0, 4],
        (first, rest) => first + new string(rest)
    );

    private static readonly Gen<string> Name = Gen.Frequency(
        (3, Gen.OneOfConst(Hostile)),
        (1, Random)
    );

    // What one case is made of: two enums, an input, the operation and two variables, by name, and the enums' values.
    private sealed record Case(
        string Enum,
        string[] EnumValues,
        string Input,
        string InputField,
        string OtherEnum,
        string[] OtherValues,
        string Operation,
        string EnumVariable,
        string InputVariable
    )
    {
        public string Schema =>
            $$"""
                schema { query: RootQuery }

                type RootQuery { operations: OperationsQueries }

                type OperationsQueries { runs(state: {{Enum}}, filter: {{Input}}): [Run!]! }

                type Run { id: ID! state: {{Enum}}! other: {{OtherEnum}} }

                enum {{Enum}} { {{string.Join(" ", EnumValues)}} }

                enum {{OtherEnum}} { {{string.Join(" ", OtherValues)}} }

                input {{Input}} { {{InputField}}: {{OtherEnum}} }
                """;

        public string Document =>
            $$"""
                query {{Operation}}(${{EnumVariable}}: {{Enum}}, ${{InputVariable}}: {{Input}}) {
                  operations { runs(state: ${{EnumVariable}}, filter: ${{InputVariable}}) { id state other } }
                }
                """;
    }

    private static readonly Gen<string[]> Values = Name.Array[1, 3];

    private static readonly Gen<Case> Cases = Gen.Select(
        Gen.Select(Name, Values, Name, Name),
        Gen.Select(Name, Values, Name, Name, Name),
        (a, b) =>
            new Case(
                a.Item1,
                a.Item2,
                a.Item3,
                a.Item4,
                b.Item1,
                b.Item2,
                b.Item3,
                b.Item4,
                b.Item5
            )
    );

    private static OperationsGenerateOptions Options(Case c) =>
        new(c.Schema, [new ClientDocumentFile("src/graphql/Runs.graphql", c.Document)]);

    [Test]
    public void The_typescript_client_is_refused_or_declares_each_name_once_as_an_identifier()
    {
        var written = 0;
        Cases.Sample(
            c =>
            {
                IReadOnlyList<ClientFile> files;
                try
                {
                    files = ClientGenerator.Render(Options(c));
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                AssertTypeScriptNames(files);
                Interlocked.Increment(ref written);
            },
            iter: 2000
        );
        written
            .Should()
            .BePositive("some of the cases are written, or the property holds of nothing");
    }

    // The client of a host's manifest, with one train a frontend's document sends: the same names, through the
    // manifest's types, the train's field and the document.
    private static ClientGenerateOptions ManifestOptions(Case c, string field) =>
        new(
            new Trax.StateMachine.ClientManifest
            {
                Trains =
                [
                    new Trax.StateMachine.ClientManifestTrain
                    {
                        Field = field,
                        Root = "mutation",
                        Answer = "Started!",
                        Arguments =
                        [
                            new Trax.StateMachine.ClientManifestArgument
                            {
                                Name = "input",
                                Type = $"{c.Input}!",
                            },
                            new Trax.StateMachine.ClientManifestArgument
                            {
                                Name = "mode",
                                Type = c.Enum,
                            },
                        ],
                    },
                ],
                Types = $$"""
                type Started { externalId: String! mode: {{c.Enum}} other: {{c.OtherEnum}} }

                enum {{c.Enum}} { {{string.Join(" ", c.EnumValues)}} }

                enum {{c.OtherEnum}} { {{string.Join(" ", c.OtherValues)}} }

                input {{c.Input}} { {{c.InputField}}: {{c.OtherEnum}} }
                """,
            },
            Documents:
            [
                new ClientDocumentFile(
                    "src/graphql/Start.graphql",
                    $$"""
                    mutation {{c.Operation}}(${{c.InputVariable}}: {{c.Input}}!, ${{c.EnumVariable}}: {{c.Enum}}) {
                      dispatch { {{field}}(input: ${{c.InputVariable}}, mode: ${{c.EnumVariable}}) { externalId mode other } }
                    }
                    """
                ),
            ]
        );

    [Test]
    public void The_manifest_client_is_refused_or_declares_each_name_once_as_an_identifier()
    {
        var written = 0;
        Gen.Select(Cases, Name)
            .Sample(
                (c, field) =>
                {
                    IReadOnlyList<ClientFile> files;
                    try
                    {
                        files = new ClientGenerator(new Fakes.FakeNodeRunner()).Render(
                            ManifestOptions(c, field)
                        );
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                    AssertTypeScriptNames(files);
                    Interlocked.Increment(ref written);
                },
                iter: 2000
            );
        written
            .Should()
            .BePositive("some of the cases are written, or the property holds of nothing");
    }

    [Test]
    public void The_csharp_client_is_refused_or_compiles()
    {
        var written = 0;
        Cases.Sample(
            c =>
            {
                IReadOnlyList<ClientFile> files;
                try
                {
                    files = ClientGenerator.RenderCSharp(
                        Options(c),
                        ClientGenerator.DefaultOperationsNamespace
                    );
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                var errors = Compile(files)
                    .Where(d => d.Severity == DiagnosticSeverity.Error || Hiding.Contains(d.Id))
                    .Select(d => d.ToString())
                    .ToList();
                errors
                    .Should()
                    .BeEmpty(
                        $"the client written for\n{c.Schema}\n{c.Document}\ncompiles, hiding no inherited member"
                    );
                Interlocked.Increment(ref written);
            },
            iter: 500
        );
        written
            .Should()
            .BePositive("some of the cases are written, or the property holds of nothing");
    }

    // The warnings a member that hides an inherited one raises. A consumer that builds with -warnaserror fails on them.
    private static readonly HashSet<string> Hiding = new(StringComparer.Ordinal)
    {
        "CS0108",
        "CS0109",
        "CS0114",
    };

    private static void AssertTypeScriptNames(IReadOnlyList<ClientFile> files)
    {
        foreach (var file in files.Where(f => f.Path.EndsWith(".ts", StringComparison.Ordinal)))
        {
            var names = TypeScriptNames(file.Text);
            names
                .Select(n => n.Name)
                .Should()
                .OnlyHaveUniqueItems($"{file.Path} declares or imports each name once");
            foreach (var (name, isType) in names)
                name.Should()
                    .MatchRegex(@"\A[A-Za-z_$][A-Za-z0-9_$]*\z")
                    .And.Match(
                        n => !TypeScriptWords.Contains(n) && !(isType && TypeOperators.Contains(n)),
                        $"{name} in {file.Path} is a TypeScript word"
                    );
        }
    }

    // The words no TypeScript declaration may take, or that no type annotation can name, read from the language rather
    // than from the generator's own list.
    private static readonly HashSet<string> TypeScriptWords = new(StringComparer.Ordinal)
    {
        "break",
        "case",
        "catch",
        "class",
        "const",
        "continue",
        "debugger",
        "default",
        "delete",
        "do",
        "else",
        "enum",
        "export",
        "extends",
        "false",
        "finally",
        "for",
        "function",
        "if",
        "import",
        "in",
        "instanceof",
        "new",
        "null",
        "return",
        "super",
        "switch",
        "this",
        "throw",
        "true",
        "try",
        "typeof",
        "var",
        "void",
        "while",
        "with",
        "implements",
        "interface",
        "let",
        "package",
        "private",
        "protected",
        "public",
        "static",
        "yield",
        "any",
        "bigint",
        "boolean",
        "number",
        "string",
        "symbol",
        "unknown",
        "never",
        "object",
        "undefined",
        "eval",
        "arguments",
    };

    // The words a value may take but no type may: where a type is read, each starts another type.
    private static readonly HashSet<string> TypeOperators = new(StringComparer.Ordinal)
    {
        "keyof",
        "readonly",
        "unique",
        "infer",
    };

    // The names a module binds, and whether each names a type: what it declares at its top level, what it imports,
    // and what it re-exports.
    private static List<(string Name, bool IsType)> TypeScriptNames(string text)
    {
        var names = new List<(string, bool)>();
        foreach (Match m in Declaration().Matches(text))
            names.Add((m.Groups[2].Value, m.Groups[1].Value is "type" or "interface"));
        foreach (Match m in ImportOrExport().Matches(text))
        foreach (
            var name in m.Groups[2]
                .Value.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                )
        )
            names.Add(
                name.StartsWith("type ", StringComparison.Ordinal)
                    ? (name[5..], true)
                    : (name, m.Groups[1].Value.Length > 0)
            );
        return names;
    }

    [GeneratedRegex(
        @"^(?:export )?(?:declare )?(?:async )?(type|interface|const|let|function|class) (?!\* )([^\s<=:({]+)",
        RegexOptions.Multiline
    )]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^(?:import|export)( type)? \{ ([^}]*) \} from", RegexOptions.Multiline)]
    private static partial Regex ImportOrExport();

    // Compiles a C# client against Trax.Client and everything deployed with the tests.
    private static IEnumerable<Diagnostic> Compile(IReadOnlyList<ClientFile> files) =>
        CSharpCompilation
            .Create(
                "GeneratedNames" + Guid.NewGuid().ToString("N"),
                files.Select(f => CSharpSyntaxTree.ParseText(f.Text, path: f.Path)),
                References.Value,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            )
            .GetDiagnostics();

    private static readonly Lazy<List<MetadataReference>> References = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList()
    );
}
