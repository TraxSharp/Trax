using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="MachineGenerator"/> orchestrates IR export (in-process) plus twin/corpus generation (node), and
/// must stage-then-place so a failed step never half-writes the tree, and diff faithfully under <c>check</c>.
/// A <see cref="FakeNodeRunner"/> stands in for node so these pin the orchestration without spawning a process;
/// real byte-parity is covered by the node integration tests.
///
/// <para>The machine id tests enforce cli/0005 (<c>docs/adr/0005-a-machine-id-is-kebab-case.md</c>).</para>
/// </summary>
[Property("adr", "docs/adr/0005-a-machine-id-is-kebab-case.md")]
public class MachineGeneratorTests
{
    private const string Adr = "see cli/0005 (docs/adr/0005-a-machine-id-is-kebab-case.md)";
    private string _root = null!;
    private string _tools = null!;
    private static readonly IMachine Turnstile = new DeclarativeTurnstileMachine();

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-gen-{Guid.NewGuid():N}");
        // A tools dir with placeholder entrypoints so the File.Exists(script) guard passes; the fake "runs" them.
        _tools = Path.Combine(_root, "engine", "tools");
        Directory.CreateDirectory(_tools);
        File.WriteAllText(Path.Combine(_tools, "generate-twin.mjs"), "// placeholder");
        File.WriteAllText(Path.Combine(_tools, "generate-corpus.mjs"), "// placeholder");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Dir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private MachineGenerateOptions Options(
        string? irOut = null,
        string? twinOut = null,
        string? corpusOut = null,
        string importStyle = "relative",
        string? specifier = null
    ) =>
        new(
            Turnstile,
            irOut,
            twinOut,
            corpusOut,
            EngineSrc: Path.Combine(_root, "engine", "src"),
            importStyle,
            specifier,
            ToolsDir: _tools
        );

    [TestCase("../x")]
    [TestCase("a/b")]
    [TestCase("")]
    [TestCase("Checkout")]
    [TestCase("ship_to_store")]
    public void An_id_that_is_not_kebab_case_is_refused_and_nothing_is_written(string id)
    {
        var node = new FakeNodeRunner();
        var irOut = Dir("ir");
        var twinOut = Dir("twin");
        var options = Options(irOut: irOut, twinOut: twinOut) with
        {
            Machine = new MachineWithId(id),
        };

        var act = () => new MachineGenerator(node).Generate(options);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*kebab-case*", "the id becomes the artifacts' file names; " + Adr);
        node.Calls.Should().BeEmpty();
        Directory
            .EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Where(p => !p.StartsWith(_tools, StringComparison.Ordinal))
            .Should()
            .BeEquivalentTo([Path.Combine(_root, "engine"), irOut, twinOut], Adr);
    }

    [TestCase("checkout")]
    [TestCase("ship-to-store")]
    [TestCase("turnstile-2")]
    public void A_kebab_case_id_is_accepted(string id)
    {
        MachineGenerator.IsMachineId(id).Should().BeTrue(Adr);
    }

    private sealed class MachineWithId(string id) : Machine<TurnstileState, TurnstileTrigger>
    {
        protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
        {
            m.Id(id).Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
            m.In(TurnstileState.Locked).On(TurnstileTrigger.Coin).To(TurnstileState.Unlocked);
        }
    }

    [Test]
    public void Generate_ir_only_writes_the_canonical_ir_and_never_touches_node()
    {
        var node = new FakeNodeRunner();
        var irOut = Dir("ir");

        var result = new MachineGenerator(node).Generate(Options(irOut: irOut));

        node.Calls.Should().BeEmpty("IR export is in-process; no node step is needed");
        var irFile = Path.Combine(irOut, "turnstile.ir.json");
        File.Exists(irFile).Should().BeTrue();
        // Canonical single-line IR plus a trailing newline, exactly what the exporter produces.
        File.ReadAllText(irFile).Should().Be(Turnstile.ExportIr() + "\n");
        result.Written.Should().ContainSingle().Which.Kind.Should().Be("ir");
    }

    [Test]
    public void Generate_places_twin_and_corpus_and_passes_the_import_style_through()
    {
        var node = new FakeNodeRunner
        {
            ContextsContent = "// ctx\n",
            MachineContent = "// mac\n",
            CorpusContent = "[]\n",
        };
        var twinOut = Dir("twin");
        var corpusOut = Dir("corpus");

        var result = new MachineGenerator(node).Generate(
            Options(twinOut: twinOut, corpusOut: corpusOut, importStyle: "specifier")
        );

        File.ReadAllText(Path.Combine(twinOut, "turnstile.contexts.g.ts")).Should().Be("// ctx\n");
        File.ReadAllText(Path.Combine(twinOut, "turnstile.machine.g.ts")).Should().Be("// mac\n");
        File.ReadAllText(Path.Combine(corpusOut, "differential.json")).Should().Be("[]\n");
        result
            .Written.Select(w => w.Kind)
            .Should()
            .BeEquivalentTo(["contexts", "machine", "corpus"]);

        // The twin call forwarded --import-style specifier to the entrypoint.
        var twinCall = node.Calls.Single(c => c.Script.Contains("twin"));
        twinCall.Args.Should().ContainInOrder("--import-style", "specifier");
    }

    [Test]
    public void Generate_forwards_a_custom_specifier()
    {
        var node = new FakeNodeRunner();
        var result = new MachineGenerator(node).Generate(
            Options(twinOut: Dir("twin"), importStyle: "specifier", specifier: "@acme/x")
        );

        result.Written.Should().NotBeEmpty();
        node.Calls.Single().Args.Should().ContainInOrder("--specifier", "@acme/x");
    }

    [Test]
    public void Generate_is_atomic_a_failed_node_step_leaves_no_output_including_the_ir()
    {
        var node = new FakeNodeRunner { ExitCode = 1, StdErr = "boom" };
        var irOut = Dir("ir");
        var twinOut = Dir("twin");

        var generate = () =>
            new MachineGenerator(node).Generate(Options(irOut: irOut, twinOut: twinOut));

        generate.Should().Throw<InvalidOperationException>().WithMessage("*boom*");
        // The IR is produced before the twin, but placement happens only after ALL steps succeed, so a twin
        // failure must leave every output root untouched.
        Directory.GetFiles(irOut).Should().BeEmpty();
        Directory.GetFiles(twinOut).Should().BeEmpty();
    }

    [Test]
    public void Generate_with_no_output_root_throws()
    {
        var generate = () => new MachineGenerator(new FakeNodeRunner()).Generate(Options());

        generate.Should().Throw<InvalidOperationException>().WithMessage("*at least one*");
    }

    [Test]
    public void Generate_without_engine_src_runs_the_bundled_generators_and_passes_them_no_engine_src()
    {
        var node = new FakeNodeRunner();
        var options = new MachineGenerateOptions(
            Turnstile,
            TwinOut: Dir("twin"),
            CorpusOut: Dir("corpus"),
            EngineSrc: null
        );

        new MachineGenerator(node, new PlacementFiles(), _tools).Generate(options);

        node.Calls.Select(c => c.Script)
            .Should()
            .Equal(
                Path.Combine(_tools, "generate-twin.mjs"),
                Path.Combine(_tools, "generate-corpus.mjs")
            );
        node.Calls.Should().OnlyContain(c => !c.Args.Contains("--engine-src"));
        File.Exists(Path.Combine(_root, "twin", "turnstile.machine.g.ts")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "corpus", "differential.json")).Should().BeTrue();
    }

    [Test]
    public void Generate_twin_without_engine_src_or_a_bundled_engine_says_to_pass_engine_src()
    {
        var options = new MachineGenerateOptions(Turnstile, TwinOut: Dir("twin"), EngineSrc: null);

        var generate = () =>
            new MachineGenerator(
                new FakeNodeRunner(),
                new PlacementFiles(),
                Dir("not-bundled")
            ).Generate(options);

        generate
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*none bundled*Pass --engine-src*");
    }

    [Test]
    public void Generate_twin_without_node_available_throws()
    {
        var node = new FakeNodeRunner { Available = false };

        var generate = () => new MachineGenerator(node).Generate(Options(twinOut: Dir("twin")));

        generate
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*node was not found on PATH*--node*");
    }

    [Test]
    public void Generate_twin_with_a_missing_entrypoint_throws()
    {
        var options = Options(twinOut: Dir("twin")) with { ToolsDir = Dir("empty-tools") };

        var generate = () => new MachineGenerator(new FakeNodeRunner()).Generate(options);

        generate.Should().Throw<InvalidOperationException>().WithMessage("*entrypoint not found*");
    }

    [Test]
    public void Check_reports_up_to_date_when_the_committed_ir_matches()
    {
        var irOut = Dir("ir");
        File.WriteAllText(Path.Combine(irOut, "turnstile.ir.json"), Turnstile.ExportIr() + "\n");

        var result = new MachineGenerator(new FakeNodeRunner()).Check(Options(irOut: irOut));

        result.IsClean.Should().BeTrue();
        result.Checks.Should().ContainSingle().Which.Status.Should().Be(DriftStatus.UpToDate);
    }

    [Test]
    public void Check_reports_drift_when_the_committed_ir_differs()
    {
        var irOut = Dir("ir");
        File.WriteAllText(Path.Combine(irOut, "turnstile.ir.json"), "{ \"stale\": true }\n");

        var result = new MachineGenerator(new FakeNodeRunner()).Check(Options(irOut: irOut));

        result.IsClean.Should().BeFalse();
        result.Checks.Single().Status.Should().Be(DriftStatus.Drifted);
    }

    [Test]
    public void Check_reports_missing_when_no_committed_artifact_exists()
    {
        var result = new MachineGenerator(new FakeNodeRunner()).Check(Options(irOut: Dir("ir")));

        result.IsClean.Should().BeFalse();
        result.Checks.Single().Status.Should().Be(DriftStatus.Missing);
    }

    [Test]
    public void Check_never_writes_to_the_output_root()
    {
        var irOut = Dir("ir");

        new MachineGenerator(new FakeNodeRunner()).Check(Options(irOut: irOut));

        Directory
            .GetFiles(irOut)
            .Should()
            .BeEmpty("check diffs against a temp copy, it must not place files");
    }

    [Test]
    public void DefaultToolsDir_ignores_a_trailing_separator_of_either_kind()
    {
        var engine = Path.GetFullPath(Path.Combine("repo", "engine"));
        var expected = Path.Combine(engine, "tools");

        MachineGenerator
            .DefaultToolsDir(Path.Combine(engine, "src") + Path.DirectorySeparatorChar)
            .Should()
            .Be(expected);
        MachineGenerator
            .DefaultToolsDir(Path.Combine(engine, "src") + Path.AltDirectorySeparatorChar)
            .Should()
            .Be(expected);
    }

    [Test]
    public void DefaultToolsDir_of_a_filesystem_root_is_refused()
    {
        var root = Path.GetPathRoot(Path.GetFullPath("."))!;

        var lookup = () => MachineGenerator.DefaultToolsDir(root);

        lookup.Should().Throw<InvalidOperationException>().WithMessage("*no parent directory*");
    }

    [Test]
    public void The_bundled_generators_sit_in_engine_beside_the_tool()
    {
        MachineGenerator
            .BundledToolsDir.Should()
            .Be(Path.Combine(AppContext.BaseDirectory, "engine"));
    }

    // The scripts come from --tools-dir, else the tools/ beside --engine-src, else the bundled copy.
    [TestCase(true, true, "custom-tools")]
    [TestCase(false, true, "engine/tools")]
    [TestCase(false, false, "bundled")]
    public void The_generators_run_from_the_first_place_that_names_them(
        bool toolsDir,
        bool engineSrc,
        string expected
    )
    {
        var node = new FakeNodeRunner();
        var bundled = Dir("bundled");
        var custom = Dir("custom-tools");
        foreach (var dir in new[] { bundled, custom })
            File.WriteAllText(Path.Combine(dir, "generate-twin.mjs"), "// placeholder");

        new MachineGenerator(node, new PlacementFiles(), bundled).Generate(
            new MachineGenerateOptions(
                Turnstile,
                TwinOut: Dir("twin"),
                EngineSrc: engineSrc ? Path.Combine(_root, "engine", "src") : null,
                ToolsDir: toolsDir ? custom : null
            )
        );

        node.Calls.Single()
            .Script.Should()
            .Be(Path.Combine(_root, Path.Combine(expected.Split('/')), "generate-twin.mjs"));
    }

    [Test]
    public void DefaultToolsDir_is_a_sibling_of_engine_src()
    {
        var engineSrc = Path.Combine("repo", "engine", "src");

        var toolsDir = MachineGenerator.DefaultToolsDir(engineSrc);

        toolsDir
            .Should()
            .Be(Path.Combine(Path.GetFullPath(Path.Combine("repo", "engine")), "tools"));
    }
}
