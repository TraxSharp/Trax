using AwesomeAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine generate</c> replaces every artifact or none: a node step that exits 0 without writing what
/// the CLI expects, or an output root that cannot be written, must leave the committed tree as it was rather
/// than place the new IR beside the old twin. <c>check</c> must report the same step as a refusal, not crash.
/// </summary>
public class MachineGeneratorPlacementTests
{
    private string _root = null!;
    private string _tools = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-place-{Guid.NewGuid():N}");
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

    private MachineGenerateOptions Options(string irOut, string twinOut) =>
        new(
            new DeclarativeTurnstileMachine(),
            irOut,
            twinOut,
            EngineSrc: Path.Combine(_root, "engine", "src"),
            ToolsDir: _tools
        );

    [Test]
    public void Generate_whose_twin_step_writes_nothing_fails_cleanly_and_places_no_ir()
    {
        var irOut = Path.Combine(_root, "ir");
        var twinOut = Path.Combine(_root, "twin");
        var node = new FakeNodeRunner { WritesOutputs = false };

        var act = () => new MachineGenerator(node).Generate(Options(irOut, twinOut));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*generate-twin.mjs exited 0 but did not write turnstile.contexts.g.ts*");
        Directory.Exists(irOut).Should().BeFalse("nothing is placed when a step fails");
    }

    [Test]
    public void Check_whose_twin_step_writes_nothing_fails_cleanly()
    {
        var irOut = Path.Combine(_root, "ir");
        var twinOut = Path.Combine(_root, "twin");
        new MachineGenerator(new FakeNodeRunner()).Generate(Options(irOut, twinOut));

        var act = () =>
            new MachineGenerator(new FakeNodeRunner { WritesOutputs = false }).Check(
                Options(irOut, twinOut)
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*did not write*");
    }

    [Test]
    public void Generate_that_cannot_place_the_twin_leaves_the_ir_root_untouched()
    {
        var irOut = Path.Combine(_root, "ir");
        Directory.CreateDirectory(irOut);
        var committedIr = Path.Combine(irOut, "turnstile.ir.json");
        File.WriteAllText(committedIr, "committed");
        // A file where the twin root's parent should be, so the twin root cannot be created.
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "");
        var twinOut = Path.Combine(blocker, "twin");

        var act = () =>
            new MachineGenerator(new FakeNodeRunner()).Generate(Options(irOut, twinOut));

        act.Should().Throw<InvalidOperationException>().WithMessage("*No artifact was changed*");
        File.ReadAllText(committedIr).Should().Be("committed");
        Directory.EnumerateFiles(irOut).Should().Equal(committedIr);
    }

    [Test]
    public void Generate_that_fails_while_swapping_puts_back_what_it_already_replaced()
    {
        var irOut = Path.Combine(_root, "ir");
        var twinOut = Path.Combine(_root, "twin");
        Directory.CreateDirectory(irOut);
        Directory.CreateDirectory(twinOut);
        var committedIr = Path.Combine(irOut, "turnstile.ir.json");
        File.WriteAllText(committedIr, "committed");
        // A directory where the twin's machine file goes: the copy beside it succeeds, the swap does not.
        Directory.CreateDirectory(Path.Combine(twinOut, "turnstile.machine.g.ts"));

        var act = () =>
            new MachineGenerator(new FakeNodeRunner()).Generate(Options(irOut, twinOut));

        act.Should().Throw<InvalidOperationException>().WithMessage("*No artifact was changed*");
        File.ReadAllText(committedIr).Should().Be("committed");
        Directory.EnumerateFiles(irOut).Should().Equal(committedIr);
        Directory.EnumerateFiles(twinOut).Should().BeEmpty();
    }

    /// <summary>Real file operations, except a move matching <see cref="FailMove"/> throws what <see cref="Throw"/> gives.</summary>
    private sealed class FailingFiles : PlacementFiles
    {
        public Func<string, string, bool> FailMove { get; init; } = (_, _) => false;
        public Func<Exception> Throw { get; init; } = () => new IOException("disk full");

        public override void Move(string source, string destination)
        {
            if (FailMove(source, destination))
                throw Throw();
            base.Move(source, destination);
        }
    }

    private static bool IsTwinSwap(string source, string destination) =>
        source.EndsWith(".tmp") && destination.EndsWith("turnstile.machine.g.ts");

    [Test]
    public void Generate_that_fails_with_any_exception_while_swapping_still_puts_everything_back()
    {
        var irOut = Path.Combine(_root, "ir");
        var twinOut = Path.Combine(_root, "twin");
        Directory.CreateDirectory(irOut);
        var committedIr = Path.Combine(irOut, "turnstile.ir.json");
        File.WriteAllText(committedIr, "committed");
        var files = new FailingFiles
        {
            FailMove = IsTwinSwap,
            Throw = () => new InvalidCastException("not an IO failure"),
        };

        var act = () =>
            new MachineGenerator(new FakeNodeRunner(), files).Generate(Options(irOut, twinOut));

        act.Should().Throw<InvalidOperationException>().WithMessage("*No artifact was changed*");
        File.ReadAllText(committedIr).Should().Be("committed");
        Directory.EnumerateFiles(irOut).Should().Equal(committedIr);
        Directory
            .Exists(twinOut)
            .Should()
            .BeFalse("the twin root did not exist before, so rolling back removes it");
    }

    [Test]
    public void Generate_that_cannot_put_an_original_back_says_where_it_was_left()
    {
        var irOut = Path.Combine(_root, "ir");
        var twinOut = Path.Combine(_root, "twin");
        Directory.CreateDirectory(irOut);
        var committedIr = Path.Combine(irOut, "turnstile.ir.json");
        File.WriteAllText(committedIr, "committed");
        var files = new FailingFiles
        {
            FailMove = (source, destination) =>
                IsTwinSwap(source, destination)
                || (source.EndsWith(".bak") && destination == committedIr),
        };

        var act = () =>
            new MachineGenerator(new FakeNodeRunner(), files).Generate(Options(irOut, twinOut));

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().NotContain("No artifact was changed");
        var backup = Directory.EnumerateFiles(irOut, "*.bak").Should().ContainSingle().Which;
        File.ReadAllText(backup).Should().Be("committed");
        message.Should().Contain($"{committedIr} is at {backup}");
    }

    [Test]
    public void Generate_that_fails_removes_only_the_directories_it_created()
    {
        var irOut = Path.Combine(_root, "ir");
        var twinParent = Path.Combine(_root, "web", "src");
        var twinOut = Path.Combine(twinParent, "machines");
        Directory.CreateDirectory(Path.Combine(_root, "web"));
        var files = new FailingFiles { FailMove = IsTwinSwap };

        var act = () =>
            new MachineGenerator(new FakeNodeRunner(), files).Generate(Options(irOut, twinOut));

        act.Should().Throw<InvalidOperationException>().WithMessage("*No artifact was changed*");
        Directory.Exists(irOut).Should().BeFalse();
        Directory.Exists(twinParent).Should().BeFalse();
        Directory
            .Exists(Path.Combine(_root, "web"))
            .Should()
            .BeTrue("it existed before generate ran");
    }
}
