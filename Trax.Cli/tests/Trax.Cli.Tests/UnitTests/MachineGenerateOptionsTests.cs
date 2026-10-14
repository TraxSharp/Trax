using AwesomeAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <c>trax machine generate</c> refuses, before it runs anything, an option that would fail inside node with a
/// message about a flag the user never passed, or that would be silently ignored; and a <c>--specifier</c> that
/// is not a package name, since it is written into the twin's import as it is.
/// </summary>
public class MachineGenerateOptionsTests
{
    private string _root = null!;
    private string _tools = null!;
    private string _engineSrc = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"trax-gen-options-{Guid.NewGuid():N}");
        _tools = Path.Combine(_root, "engine", "tools");
        _engineSrc = Path.Combine(_root, "engine", "src");
        Directory.CreateDirectory(_tools);
        Directory.CreateDirectory(_engineSrc);
        File.WriteAllText(Path.Combine(_tools, "generate-twin.mjs"), "// placeholder");
        File.WriteAllText(Path.Combine(_tools, "generate-corpus.mjs"), "// placeholder");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Twin => Path.Combine(_root, "twin");

    private void ShouldRefuse(MachineGenerateOptions options, string message)
    {
        var node = new FakeNodeRunner();

        var generate = () =>
            new MachineGenerator(node, new PlacementFiles(), _tools).Generate(options);

        generate.Should().Throw<InvalidOperationException>().WithMessage(message);
        node.Calls.Should().BeEmpty("the options are refused before node runs");
        Directory.Exists(Twin).Should().BeFalse("nothing is written");
    }

    [Test]
    public void Tools_dir_without_engine_src_is_refused_up_front()
    {
        ShouldRefuse(
            new(new DeclarativeTurnstileMachine(), TwinOut: Twin, ToolsDir: _tools),
            "--tools-dir needs --engine-src*"
        );
    }

    [Test]
    public void Relative_imports_without_engine_src_are_refused()
    {
        ShouldRefuse(
            new(new DeclarativeTurnstileMachine(), TwinOut: Twin, ImportStyle: "relative"),
            "--import-style relative*needs --engine-src*"
        );
    }

    [TestCase("relative")]
    [TestCase(null)]
    public void A_specifier_with_relative_imports_is_refused(string? importStyle)
    {
        // With --engine-src, relative is also the default.
        ShouldRefuse(
            new(
                new DeclarativeTurnstileMachine(),
                TwinOut: Twin,
                EngineSrc: _engineSrc,
                ImportStyle: importStyle,
                Specifier: "@acme/engine",
                ToolsDir: _tools
            ),
            "--specifier applies only to --import-style specifier*"
        );
    }

    [TestCase("")]
    [TestCase("@")]
    [TestCase("@/engine")]
    [TestCase("@scope/")]
    [TestCase("a\"b")]
    [TestCase("x\"; import \"evil")]
    [TestCase("Engine")]
    [TestCase("has space")]
    [TestCase(".hidden")]
    [TestCase("_private")]
    [TestCase("../engine")]
    [TestCase("./engine")]
    [TestCase("engine/../../x")]
    [TestCase("engine/")]
    public void A_specifier_that_is_not_a_package_name_is_refused(string specifier)
    {
        MachineGenerator.IsModuleSpecifier(specifier).Should().BeFalse();
        ShouldRefuse(
            new(
                new DeclarativeTurnstileMachine(),
                TwinOut: Twin,
                ImportStyle: "specifier",
                Specifier: specifier
            ),
            "--specifier must be an npm package name*"
        );
    }

    [TestCase("@traxsharp/state-machine")]
    [TestCase("state-machine")]
    [TestCase("@acme/engine/machines")]
    [TestCase("@acme/engine.v2/Typed_Twin.js")]
    [TestCase("engine~x")]
    public void A_package_name_with_an_optional_subpath_is_a_specifier(string specifier)
    {
        MachineGenerator.IsModuleSpecifier(specifier).Should().BeTrue();
    }

    [Test]
    public void A_package_name_longer_than_npm_allows_is_refused()
    {
        MachineGenerator.IsModuleSpecifier(new string('a', 215)).Should().BeFalse();
        MachineGenerator.IsModuleSpecifier(new string('a', 214)).Should().BeTrue();
    }

    [Test]
    public void A_valid_specifier_reaches_the_bundled_generator()
    {
        var node = new FakeNodeRunner();

        new MachineGenerator(node, new PlacementFiles(), _tools).Generate(
            new(new DeclarativeTurnstileMachine(), TwinOut: Twin, Specifier: "@acme/engine/twin")
        );

        node.Calls.Single()
            .Args.Should()
            .ContainInOrder("--import-style", "specifier", "--specifier", "@acme/engine/twin");
    }

    [Test]
    public void A_bundled_generator_that_writes_nothing_is_not_blamed_on_engine_src()
    {
        var generate = () =>
            new MachineGenerator(
                new FakeNodeRunner { WritesOutputs = false },
                new PlacementFiles(),
                _tools
            ).Generate(new(new DeclarativeTurnstileMachine(), TwinOut: Twin));

        generate
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*did not write*generators bundled into this trax*reinstall trax*")
            .Which.Message.Should()
            .NotContain("--engine-src");
    }

    [Test]
    public void An_engine_checkout_that_writes_nothing_is_named()
    {
        var generate = () =>
            new MachineGenerator(new FakeNodeRunner { WritesOutputs = false }).Generate(
                new(new DeclarativeTurnstileMachine(), TwinOut: Twin, EngineSrc: _engineSrc)
            );

        generate
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*did not write*The engine at --engine-src ({_engineSrc})*");
    }

    [Test]
    public void Generators_in_tools_dir_that_write_nothing_are_named()
    {
        var generate = () =>
            new MachineGenerator(new FakeNodeRunner { WritesOutputs = false }).Generate(
                new(
                    new DeclarativeTurnstileMachine(),
                    TwinOut: Twin,
                    EngineSrc: _engineSrc,
                    ToolsDir: _tools
                )
            );

        generate
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*did not write*The generators in --tools-dir ({_tools})*");
    }
}
