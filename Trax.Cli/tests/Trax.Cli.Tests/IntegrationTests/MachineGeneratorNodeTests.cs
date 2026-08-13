using FluentAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// End-to-end byte-parity: the CLI, running real node against the sibling Trax.Api.StateMachine engine, must
/// reproduce the committed turnstile artifacts exactly. This is the whole point of the tool, so it is proven
/// against the real generators, not a fake. Skipped at runtime (never a hard failure) when node is unavailable
/// or the sibling engine is not checked out beside this repo, per the repo's determinism rules.
/// </summary>
public class MachineGeneratorNodeTests
{
    private static readonly IMachine Turnstile = new DeclarativeTurnstileMachine();

    private string _out = null!;
    private string _engineSrc = null!;

    [SetUp]
    public void SetUp()
    {
        if (!new NodeRunner().IsAvailable())
            Assert.Ignore("node is not on PATH; skipping the real-node byte-parity test.");

        var engineRoot = FindEngineRoot();
        if (engineRoot is null)
            Assert.Ignore(
                "Trax.Api.StateMachine is not checked out beside Trax.Cli; skipping byte-parity test."
            );

        _engineSrc = Path.Combine(engineRoot!, "src");
        _out = Path.Combine(Path.GetTempPath(), $"trax-machine-it-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_out);
    }

    [TearDown]
    public void TearDown()
    {
        if (_out is not null && Directory.Exists(_out))
            Directory.Delete(_out, recursive: true);
    }

    [Test]
    public void Generate_produces_canonical_ir_a_full_twin_and_a_corpus_and_is_idempotent()
    {
        var generator = new MachineGenerator(new NodeRunner());
        var first = GenerateInto("a");
        var second = GenerateInto("b");

        // IR: the CLI writes the canonical single-line export plus a trailing newline (not the pretty format
        // the engine repo's older committed turnstile.ir.json still carries; that is retired in Increment E).
        var ir = File.ReadAllText(Path.Combine(_out, "a", "ir", "turnstile.ir.json"));
        ir.Should().Be(Turnstile.ExportIr() + "\n");
        ir.TrimEnd('\n').Should().NotContain("\n", "the on-disk IR is canonical single-line");

        // The real generators produced a full twin and a non-trivial corpus.
        var contexts = Path.Combine(_out, "a", "twin", "turnstile.contexts.g.ts");
        var machine = Path.Combine(_out, "a", "twin", "turnstile.machine.g.ts");
        var corpus = Path.Combine(_out, "a", "corpus", "differential.json");
        File.ReadAllText(contexts).Should().Contain("export type TurnstileState");
        File.ReadAllText(machine).Should().Contain("machineFromIr");
        File.ReadAllText(corpus).Should().Contain("\"cases\"");

        // Idempotent: a second run against the same source is byte-identical, which is what makes check sound.
        FilesShouldMatch(machine, Path.Combine(_out, "b", "twin", "turnstile.machine.g.ts"));
        FilesShouldMatch(corpus, Path.Combine(_out, "b", "corpus", "differential.json"));
        first
            .Written.Select(w => w.Kind)
            .Should()
            .BeEquivalentTo(second.Written.Select(w => w.Kind));
    }

    private GenerateResult GenerateInto(string sub) =>
        new MachineGenerator(new NodeRunner()).Generate(
            new MachineGenerateOptions(
                Turnstile,
                Path.Combine(_out, sub, "ir"),
                Path.Combine(_out, sub, "twin"),
                Path.Combine(_out, sub, "corpus"),
                EngineSrc: _engineSrc,
                ImportStyle: "relative"
            )
        );

    [Test]
    public void Generate_reproduces_the_committed_turnstile_artifacts_byte_for_byte() =>
        AssertByteParityWithCommitted(new DeclarativeTurnstileMachine(), "turnstile");

    [Test]
    public void Generate_reproduces_the_committed_checkout_artifacts_byte_for_byte() =>
        AssertByteParityWithCommitted(new DeclarativeCheckoutMachine(), "checkout");

    // The end goal: the CLI's IR/twin/corpus for a machine are byte-identical to what the engine repo commits,
    // so `trax machine generate` (and `check`) is a faithful stand-in for the retired regenerate-via-tests path.
    private void AssertByteParityWithCommitted(IMachine machine, string id)
    {
        var irOut = Path.Combine(_out, id, "ir");
        var twinOut = Path.Combine(_out, id, "twin");
        var corpusOut = Path.Combine(_out, id, "corpus");

        new MachineGenerator(new NodeRunner()).Generate(
            new MachineGenerateOptions(
                machine,
                irOut,
                twinOut,
                corpusOut,
                EngineSrc: _engineSrc,
                ImportStyle: "relative"
            )
        );

        var engineRoot = Directory.GetParent(_engineSrc)!.FullName;
        FilesShouldMatch(
            Path.Combine(irOut, $"{id}.ir.json"),
            Path.Combine(engineRoot, "machines", id, $"{id}.ir.json")
        );
        FilesShouldMatch(
            Path.Combine(twinOut, $"{id}.contexts.g.ts"),
            Path.Combine(engineRoot, "src", "machines", id, $"{id}.contexts.g.ts")
        );
        FilesShouldMatch(
            Path.Combine(twinOut, $"{id}.machine.g.ts"),
            Path.Combine(engineRoot, "src", "machines", id, $"{id}.machine.g.ts")
        );
        FilesShouldMatch(
            Path.Combine(corpusOut, "differential.json"),
            Path.Combine(engineRoot, "machines", id, "differential.json")
        );
    }

    [Test]
    public void Check_is_clean_against_freshly_generated_artifacts()
    {
        var options = new MachineGenerateOptions(
            Turnstile,
            IrOut: Path.Combine(_out, "ir"),
            TwinOut: Path.Combine(_out, "twin"),
            CorpusOut: Path.Combine(_out, "corpus"),
            EngineSrc: _engineSrc,
            ImportStyle: "relative"
        );
        var generator = new MachineGenerator(new NodeRunner());

        generator.Generate(options);
        var check = generator.Check(options);

        check.IsClean.Should().BeTrue();
        check.Checks.Should().OnlyContain(c => c.Status == DriftStatus.UpToDate);
    }

    private static void FilesShouldMatch(string generated, string committed)
    {
        File.Exists(committed).Should().BeTrue($"committed artifact should exist: {committed}");
        File.ReadAllBytes(generated)
            .Should()
            .Equal(
                File.ReadAllBytes(committed),
                $"{Path.GetFileName(generated)} must be byte-identical"
            );
    }

    // Walk up from the test binary to the workspace root and look for the sibling engine repo.
    private static string? FindEngineRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Trax.Api.StateMachine");
            if (Directory.Exists(Path.Combine(candidate, "src", "rules")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
