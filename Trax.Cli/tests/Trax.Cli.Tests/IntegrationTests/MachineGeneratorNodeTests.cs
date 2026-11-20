using AwesomeAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Core.Extensions;
using Trax.StateMachine.Persistence;

namespace Trax.Cli.Tests.IntegrationTests;

/// <summary>
/// End-to-end byte-parity: the CLI, running real node against the sibling Trax.StateMachine/ts engine, must
/// reproduce the committed turnstile artifacts exactly. This is the whole point of the tool, so it is proven
/// against the real generators, not a fake. Skipped at runtime when node is unavailable or the sibling engine is
/// not checked out beside this repo, except under CI, where that fails the test.
/// </summary>
public class MachineGeneratorNodeTests
{
    private static readonly IMachine Turnstile = new DeclarativeTurnstileMachine();

    private string _out = "";
    private string _engineSrc = "";

    [SetUp]
    public async Task SetUp()
    {
        if (!(await new NodeRunner().IsAvailableAsync().ConfigureAwait(false)))
            NodePrerequisite.Missing(
                "node is not on PATH; skipping the real-node byte-parity test."
            );

        var engineRoot = FindEngineRoot();
        if (engineRoot is null)
            NodePrerequisite.Missing(
                "Trax.StateMachine/ts is not checked out beside Trax.Cli; skipping byte-parity test."
            );

        _engineSrc = Path.Combine(engineRoot.NotNull(), "src");
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
    public async Task Generate_produces_canonical_ir_a_full_twin_and_a_corpus_and_is_idempotent()
    {
        var generator = new MachineGenerator(new NodeRunner());
        var first = await GenerateIntoAsync("a").ConfigureAwait(false);
        var second = await GenerateIntoAsync("b").ConfigureAwait(false);

        // IR: the CLI writes the canonical single-line export plus a trailing newline (not the pretty format
        // the engine repo's older committed turnstile.ir.json still carries; that is retired in Increment E).
        var ir = await File.ReadAllTextAsync(Path.Combine(_out, "a", "ir", "turnstile.ir.json"))
            .ConfigureAwait(false);
        ir.Should().Be(Turnstile.ExportIr() + "\n");
        ir.TrimEnd('\n').Should().NotContain("\n", "the on-disk IR is canonical single-line");

        // The real generators produced a full twin and a non-trivial corpus.
        var contexts = Path.Combine(_out, "a", "twin", "turnstile.contexts.g.ts");
        var machine = Path.Combine(_out, "a", "twin", "turnstile.machine.g.ts");
        var corpus = Path.Combine(_out, "a", "corpus", "differential.json");
        (await File.ReadAllTextAsync(contexts).ConfigureAwait(false))
            .Should()
            .Contain("export type TurnstileState");
        (await File.ReadAllTextAsync(machine).ConfigureAwait(false))
            .Should()
            .Contain("typedMachineFromIr");
        (await File.ReadAllTextAsync(corpus).ConfigureAwait(false)).Should().Contain("\"cases\"");

        // Idempotent: a second run against the same source is byte-identical, which is what makes check sound.
        await FilesShouldMatch(machine, Path.Combine(_out, "b", "twin", "turnstile.machine.g.ts"))
            .ConfigureAwait(false);
        await FilesShouldMatch(corpus, Path.Combine(_out, "b", "corpus", "differential.json"))
            .ConfigureAwait(false);
        first
            .Written.Select(w => w.Kind)
            .Should()
            .BeEquivalentTo(second.Written.Select(w => w.Kind));
    }

    private async Task<GenerateResult> GenerateIntoAsync(string sub) =>
        await new MachineGenerator(new NodeRunner())
            .GenerateAsync(
                new MachineGenerateOptions(
                    MachineIr.Of(Turnstile),
                    Path.Combine(_out, sub, "ir"),
                    Path.Combine(_out, sub, "twin"),
                    Path.Combine(_out, sub, "corpus"),
                    EngineSrc: _engineSrc,
                    ImportStyle: "relative"
                )
            )
            .ConfigureAwait(false);

    [Test]
    public async Task Generate_reproduces_the_committed_turnstile_artifacts_byte_for_byte() =>
        await AssertByteParityWithCommittedAsync(new DeclarativeTurnstileMachine(), "turnstile")
            .ConfigureAwait(false);

    [Test]
    public async Task Generate_reproduces_the_committed_checkout_artifacts_byte_for_byte() =>
        await AssertByteParityWithCommittedAsync(new DeclarativeCheckoutMachine(), "checkout")
            .ConfigureAwait(false);

    // A state that invokes a train exports outcome triggers, which the twin and corpus generators carry through.
    [Test]
    public async Task Generate_reproduces_the_committed_ingest_artifacts_with_outcome_triggers_byte_for_byte() =>
        await AssertByteParityWithCommittedAsync(new DeclarativeIngestMachine(), "ingest")
            .ConfigureAwait(false);

    // Guards that compare with lt, gte and lte, whose corpus holds both runtimes to the same bounds.
    [Test]
    public async Task Generate_reproduces_the_committed_gauge_artifacts_byte_for_byte() =>
        await AssertByteParityWithCommittedAsync(new DeclarativeGaugeMachine(), "gauge")
            .ConfigureAwait(false);

    // A timed state exports its timer as a trigger of its own kind, which the twin shows and never fires and the
    // corpus replays through AdvanceTimer. The committed reminder artifacts are written by the generation this
    // test waits for, so it first proves the generated IR carries the timer.
    [Test]
    [Property(
        "adr",
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
    )]
    public async Task Generate_reproduces_the_committed_reminder_artifacts_with_timers_byte_for_byte()
    {
        var reminder = new DeclarativeReminderMachine();

        reminder
            .ExportIr()
            .Should()
            .Contain("\"timers\"", "a timed state exports its timer (Trax.Docs/adr/0048)")
            .And.Contain("Review.after")
            .And.Contain("PT10M");

        await AssertByteParityWithCommittedAsync(reminder, "reminder").ConfigureAwait(false);
    }

    // The end goal: the CLI's IR/twin/corpus for a machine are byte-identical to what the engine repo commits,
    // so `trax machine generate` (and `check`) is a faithful stand-in for the retired regenerate-via-tests path.
    private async Task AssertByteParityWithCommittedAsync(IMachine machine, string id)
    {
        var irOut = Path.Combine(_out, id, "ir");
        var twinOut = Path.Combine(_out, id, "twin");
        var corpusOut = Path.Combine(_out, id, "corpus");

        await new MachineGenerator(new NodeRunner())
            .GenerateAsync(
                new MachineGenerateOptions(
                    MachineIr.Of(machine),
                    irOut,
                    twinOut,
                    corpusOut,
                    EngineSrc: _engineSrc,
                    ImportStyle: "relative"
                )
            )
            .ConfigureAwait(false);

        var engineRoot = Directory.GetParent(_engineSrc).NotNull().FullName;
        var machinesRoot = Path.Combine(engineRoot, "..", "conformance", "machines");
        await FilesShouldMatch(
                Path.Combine(irOut, $"{id}.ir.json"),
                Path.Combine(machinesRoot, id, $"{id}.ir.json")
            )
            .ConfigureAwait(false);
        await FilesShouldMatch(
                Path.Combine(twinOut, $"{id}.contexts.g.ts"),
                Path.Combine(engineRoot, "src", "machines", id, $"{id}.contexts.g.ts")
            )
            .ConfigureAwait(false);
        await FilesShouldMatch(
                Path.Combine(twinOut, $"{id}.machine.g.ts"),
                Path.Combine(engineRoot, "src", "machines", id, $"{id}.machine.g.ts")
            )
            .ConfigureAwait(false);
        await FilesShouldMatch(
                Path.Combine(corpusOut, "differential.json"),
                Path.Combine(machinesRoot, id, "differential.json")
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task Check_is_clean_against_freshly_generated_artifacts()
    {
        var options = new MachineGenerateOptions(
            MachineIr.Of(Turnstile),
            IrOut: Path.Combine(_out, "ir"),
            TwinOut: Path.Combine(_out, "twin"),
            CorpusOut: Path.Combine(_out, "corpus"),
            EngineSrc: _engineSrc,
            ImportStyle: "relative"
        );
        var generator = new MachineGenerator(new NodeRunner());

        await generator.GenerateAsync(options).ConfigureAwait(false);
        var check = await generator.CheckAsync(options).ConfigureAwait(false);

        check.IsClean.Should().BeTrue();
        check.Checks.Should().OnlyContain(c => c.Status == DriftStatus.UpToDate);
    }

    private static async Task FilesShouldMatch(string generated, string committed)
    {
        File.Exists(committed).Should().BeTrue($"committed artifact should exist: {committed}");
        (await File.ReadAllBytesAsync(generated).ConfigureAwait(false))
            .Should()
            .Equal(
                await File.ReadAllBytesAsync(committed).ConfigureAwait(false),
                $"{Path.GetFileName(generated)} must be byte-identical"
            );
    }

    // Walk up from the test binary to the workspace root and look for the sibling engine repo.
    private static string? FindEngineRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Trax.StateMachine", "ts");
            if (Directory.Exists(Path.Combine(candidate, "src", "rules")))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
