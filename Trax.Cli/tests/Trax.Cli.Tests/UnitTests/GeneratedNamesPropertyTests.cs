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

    // The names above, drawn from a fixed seed so a failure is the same on every run, with a fragment each document
    // spreads and, in the manifest client, the run progress module. The regexes above read names; this runs the
    // compiler over what is written, so a module that does not parse or names a type it does not declare or import
    // fails here rather than in a consumer's build.
    [Test]
    public void A_sample_of_the_typescript_clients_typechecks()
    {
        var engine = TypeScriptEngine();
        var root = Directory.CreateTempSubdirectory("trax-ts-names-");
        try
        {
            var random = new System.Random(20261105);
            string Pick() =>
                random.Next(4) == 0
                    ? "abAB_"[random.Next(5)]
                        + new string(
                            Enumerable
                                .Range(0, random.Next(5))
                                .Select(_ => "abAB_09"[random.Next(7)])
                                .ToArray()
                        )
                    : Hostile[random.Next(Hostile.Length)];
            var operations = 0;
            var manifests = 0;
            for (var i = 0; i < 4000 && (operations < 40 || manifests < 40); i++)
            {
                var c = new Case(
                    Pick(),
                    [Pick(), Pick()],
                    Pick(),
                    Pick(),
                    Pick(),
                    [Pick()],
                    Pick(),
                    Pick(),
                    Pick()
                );
                var fragment = Pick();
                if (
                    operations < 40
                    && TryWrite(root, $"operations-{i}", OperationsWithFragment(c, fragment))
                )
                    operations++;
                if (
                    manifests < 40
                    && TryWrite(root, $"manifest-{i}", ManifestWithProgress(c, Pick(), fragment))
                )
                    manifests++;
            }
            operations.Should().Be(40, "the seed writes enough operations clients to check");
            manifests.Should().Be(40, "the seed writes enough manifest clients to check");

            File.WriteAllText(
                Path.Combine(root.FullName, "tsconfig.json"),
                $$"""
                {
                  "compilerOptions": {
                    "target": "es2022",
                    "lib": ["ES2022", "DOM", "DOM.Iterable"],
                    "module": "esnext",
                    "moduleResolution": "bundler",
                    "moduleDetection": "force",
                    "jsx": "react-jsx",
                    "verbatimModuleSyntax": true,
                    "strict": true,
                    "noUnusedLocals": true,
                    "noUnusedParameters": true,
                    "noFallthroughCasesInSwitch": true,
                    "skipLibCheck": true,
                    "types": ["node"],
                    "noEmit": true,
                    "paths": {
                      "@traxsharp/state-machine": [{{Json(
                    Path.Combine(engine, "src", "index.ts")
                )}}],
                      "@traxsharp/state-machine/client": [{{Json(
                    Path.Combine(engine, "src", "client", "index.ts")
                )}}],
                      "@traxsharp/state-machine/react": [{{Json(
                    Path.Combine(engine, "src", "react.ts")
                )}}]
                    }
                  },
                  "include": ["cases"]
                }
                """
            );
            Directory.CreateSymbolicLink(
                Path.Combine(root.FullName, "node_modules"),
                Path.Combine(engine, "node_modules")
            );

            var (code, output) = Node(
                root.FullName,
                Path.Combine(engine, "node_modules", "typescript", "bin", "tsc"),
                "-p",
                "tsconfig.json"
            );

            var failing = CaseFolder()
                .Matches(output)
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Select(folder =>
                    File.ReadAllText(Path.Combine(root.FullName, "cases", folder, "case.graphql"))
                );
            code.Should()
                .Be(0, $"every sampled client typechecks:\n{output}\n{string.Join("\n", failing)}");
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    // Writes the client `render` makes under cases/<folder>, beside the schema and documents it was made from; false
    // when the generator refuses them.
    private static bool TryWrite(
        DirectoryInfo root,
        string folder,
        (Func<IReadOnlyList<ClientFile>> Render, string Source) client
    )
    {
        IReadOnlyList<ClientFile> files;
        try
        {
            files = client.Render();
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        var dir = Path.Combine(root.FullName, "cases", folder);
        foreach (var file in files)
        {
            var path = Path.Combine(dir, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Text);
        }
        File.WriteAllText(Path.Combine(dir, "case.graphql"), $"# {folder}\n{client.Source}\n");
        return true;
    }

    private static (Func<IReadOnlyList<ClientFile>>, string) OperationsWithFragment(
        Case c,
        string fragment
    )
    {
        var document = $$"""
            query {{c.Operation}}(${{c.EnumVariable}}: {{c.Enum}}, ${{c.InputVariable}}: {{c.Input}}) {
              operations { runs(state: ${{c.EnumVariable}}, filter: ${{c.InputVariable}}) { ...{{fragment}} state } }
            }

            fragment {{fragment}} on Run { id other }
            """;
        return (
            () =>
                ClientGenerator.Render(
                    new OperationsGenerateOptions(
                        c.Schema,
                        [new ClientDocumentFile("src/graphql/Runs.graphql", document)]
                    )
                ),
            $"{c.Schema}\n{document}"
        );
    }

    private static (Func<IReadOnlyList<ClientFile>>, string) ManifestWithProgress(
        Case c,
        string field,
        string fragment
    )
    {
        var options = ManifestOptions(c, field);
        var document = $$"""
            mutation {{c.Operation}}(${{c.InputVariable}}: {{c.Input}}!, ${{c.EnumVariable}}: {{c.Enum}}) {
              dispatch { {{field}}(input: ${{c.InputVariable}}, mode: ${{c.EnumVariable}}) { ...{{fragment}} mode } }
            }

            fragment {{fragment}} on Started { externalId other }
            """;
        var sample = Fakes.ClientManifests.Sample();
        var manifest = new Trax.StateMachine.ClientManifest
        {
            Trains = options.Manifest.Trains.Select(t => t with { Progress = true }).ToList(),
            Progress = sample.Progress,
            Types = options.Manifest.Types + "\n" + ProgressTypes,
        };
        return (
            () =>
                new ClientGenerator(new Fakes.FakeNodeRunner()).Render(
                    options with
                    {
                        Manifest = manifest,
                        Documents = [new ClientDocumentFile("src/graphql/Start.graphql", document)],
                    }
                ),
            $"{manifest.Types}\n{document}"
        );
    }

    // The types the run progress documents select, from the sample manifest.
    private static readonly string ProgressTypes = Fakes.ClientManifests.TrainTypes[
        Fakes.ClientManifests.TrainTypes.IndexOf("type RunProgress {", StringComparison.Ordinal)..
    ];

    private static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    [GeneratedRegex(@"cases/([^/(]+)/")]
    private static partial Regex CaseFolder();

    // Trax.StateMachine/ts with its npm packages, whose TypeScript, graphql-ws and React the check compiles against.
    private static string TypeScriptEngine()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            var engine = Path.Combine(dir.FullName, "Trax.StateMachine", "ts");
            if (File.Exists(Path.Combine(dir.FullName, "global.json")) && Directory.Exists(engine))
            {
                if (!File.Exists(Path.Combine(engine, "node_modules", "typescript", "bin", "tsc")))
                    IntegrationTests.NodePrerequisite.Missing(
                        "Trax.StateMachine/ts has no node_modules; run `npm ci` there to typecheck the clients."
                    );
                return engine;
            }
        }
        IntegrationTests.NodePrerequisite.Missing(
            "The repository is not checked out around this build."
        );
        return "";
    }

    private static (int ExitCode, string Output) Node(string workingDirectory, params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("node")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        System.Diagnostics.Process process;
        try
        {
            process = System.Diagnostics.Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            IntegrationTests.NodePrerequisite.Missing(
                "node is not on PATH; the TypeScript check needs it."
            );
            return (0, "");
        }
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
            {
                process.Kill(entireProcessTree: true);
                return (-1, "tsc did not finish within five minutes.");
            }
            return (process.ExitCode, stdout.Result + stderr.Result);
        }
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
