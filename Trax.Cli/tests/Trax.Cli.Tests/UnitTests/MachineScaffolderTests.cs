using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Machines;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="MachineScaffolder"/> emits a machine's starter C# file. The scaffold's whole value is that it
/// compiles and runs through the rest of the pipeline as-is, so beyond the structural assertions these
/// actually compile the generated source against the real Trax.Effect.StateMachine assemblies.
/// </summary>
public class MachineScaffolderTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"trax-scaffold-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [TestCase("checkout", ExpectedResult = "Checkout")]
    [TestCase("write-to-congress", ExpectedResult = "WriteToCongress")]
    [TestCase("order_flow", ExpectedResult = "OrderFlow")]
    [TestCase("TURNSTILE", ExpectedResult = "Turnstile")]
    public string Pascal_capitalizes_each_word(string name) => MachineScaffolder.Pascal(name);

    [TestCase("write-to-congress", ExpectedResult = "write-to-congress")]
    [TestCase("OrderFlow", ExpectedResult = "orderflow")]
    [TestCase("check out", ExpectedResult = "check-out")]
    public string Kebab_lowercases_and_dashes(string name) => MachineScaffolder.Kebab(name);

    [Test]
    public void Render_without_effect_emits_the_declarative_skeleton_and_no_effect()
    {
        var source = MachineScaffolder.Render("checkout", "MyApp.Machines", withEffect: false);

        source.Should().Contain("namespace MyApp.Machines;");
        source.Should().Contain("public enum CheckoutState");
        source.Should().Contain("public enum CheckoutTrigger");
        source.Should().Contain("public sealed record CheckoutContext");
        source
            .Should()
            .Contain(
                "public sealed class CheckoutMachine : Machine<CheckoutState, CheckoutTrigger>"
            );
        source.Should().Contain("m.Id(\"checkout\")");
        source.Should().Contain("Input((CheckoutSubmitInput i) => i.Value).NonEmpty()");
        source.Should().Contain("Set((CheckoutContext c) => c.Value).FromInput");
        source.Should().Contain("m.Differential(");

        source.Should().NotContain("ISnapshotEffect");
        source.Should().NotContain("RunsOnce");
        source.Should().NotContain(".Committed()");
    }

    [Test]
    public void Render_with_effect_emits_the_effect_stub_and_a_committed_terminal_state()
    {
        var source = MachineScaffolder.Render("checkout", "MyApp.Machines", withEffect: true);

        source.Should().Contain("public interface ICheckoutEffect : ISnapshotEffect;");
        source.Should().Contain("public sealed class CheckoutEffect : ICheckoutEffect");
        source.Should().Contain(".RunsOnce<ICheckoutEffect>(\"checkout:submit\")");
        source.Should().Contain("m.In(CheckoutState.Done).Committed().Context<CheckoutContext>();");
    }

    [Test]
    public void Write_creates_the_named_file_and_returns_its_path()
    {
        var path = MachineScaffolder.Write(
            "checkout",
            _dir,
            "MyApp",
            withEffect: false,
            force: false
        );

        path.Should().Be(Path.Combine(_dir, "CheckoutMachine.cs"));
        File.Exists(path).Should().BeTrue();
    }

    [Test]
    public void Write_refuses_to_overwrite_without_force()
    {
        MachineScaffolder.Write("checkout", _dir, "MyApp", withEffect: false, force: false);

        var write = () => MachineScaffolder.Write("checkout", _dir, "MyApp", false, force: false);

        write.Should().Throw<InvalidOperationException>().WithMessage("*already exists*--force*");
    }

    [Test]
    public void Write_overwrites_with_force()
    {
        var path = MachineScaffolder.Write("checkout", _dir, "MyApp", false, force: false);
        File.WriteAllText(path, "// clobbered");

        MachineScaffolder.Write("checkout", _dir, "MyApp", false, force: true);

        File.ReadAllText(path).Should().Contain("public sealed class CheckoutMachine");
    }

    [Test]
    public void Write_rejects_an_empty_name()
    {
        var write = () => MachineScaffolder.Write("   ", _dir, "MyApp", false, false);

        write.Should().Throw<InvalidOperationException>().WithMessage("*name is required*");
    }

    [Test]
    public void The_scaffold_compiles_against_the_real_engine_without_an_effect()
    {
        AssertCompiles(MachineScaffolder.Render("checkout", "Scaffolded", withEffect: false));
    }

    [Test]
    public void The_scaffold_compiles_against_the_real_engine_with_an_effect()
    {
        AssertCompiles(MachineScaffolder.Render("checkout", "Scaffolded", withEffect: true));
    }

    // Compile the generated source against the real Trax.Effect.StateMachine assemblies. This is the honest
    // proof that the scaffold is not just well-shaped text but valid, buildable C#.
    private static void AssertCompiles(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.Ordinal))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
        // The Trax.Effect.* assemblies the scaffold binds against (present in the test's output directory).
        foreach (var dll in Directory.GetFiles(AppContext.BaseDirectory, "Trax.Effect*.dll"))
            references.Add(MetadataReference.CreateFromFile(dll));

        var compilation = CSharpCompilation.Create(
            "ScaffoldCompileTest",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable
            )
        );

        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        errors
            .Should()
            .BeEmpty(because: "the scaffold must compile:\n" + string.Join("\n", errors));
    }

    // Referenced so the Trax.Effect.StateMachine.Persistence assembly is copied to the test output for the
    // compile references above.
    private static readonly Type _keepPersistenceReferenced = typeof(IMachine);
}
