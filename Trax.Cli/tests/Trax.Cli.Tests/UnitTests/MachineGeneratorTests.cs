using FluentAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="MachineGenerator"/> orchestrates IR export (in-process) plus twin/corpus generation (node), and
/// must stage-then-place so a failed step never half-writes the tree, and diff faithfully under <c>check</c>.
/// A <see cref="FakeNodeRunner"/> stands in for node so these pin the orchestration without spawning a process;
/// real byte-parity is covered by the node integration tests.
/// </summary>
public class MachineGeneratorTests
{
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
            EngineSrc: twinOut is null && corpusOut is null
                ? null
                : Path.Combine(_root, "engine", "src"),
            importStyle,
            specifier,
            ToolsDir: _tools
        );

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
    public void Generate_twin_without_engine_src_throws()
    {
        var options = new MachineGenerateOptions(Turnstile, TwinOut: Dir("twin"), EngineSrc: null);

        var generate = () => new MachineGenerator(new FakeNodeRunner()).Generate(options);

        generate.Should().Throw<InvalidOperationException>().WithMessage("*--engine-src*");
    }

    [Test]
    public void Generate_twin_without_node_available_throws()
    {
        var node = new FakeNodeRunner { Available = false };

        var generate = () => new MachineGenerator(node).Generate(Options(twinOut: Dir("twin")));

        generate.Should().Throw<InvalidOperationException>().WithMessage("*node on PATH*");
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
    public void DefaultToolsDir_is_a_sibling_of_engine_src()
    {
        var engineSrc = Path.Combine("repo", "engine", "src");

        var toolsDir = MachineGenerator.DefaultToolsDir(engineSrc);

        toolsDir
            .Should()
            .Be(Path.Combine(Path.GetFullPath(Path.Combine("repo", "engine")), "tools"));
    }
}
