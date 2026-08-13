using System.CommandLine;
using FluentAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Tests.Fakes;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The <c>trax machine</c> command surface and its exit-code contract: a bad option or a load failure exits 1,
/// a clean generate/check exits 0, and <c>check</c> exits 1 on drift (the CI gate). The handlers return the
/// exit code (Program returns InvokeAsync's result, so Environment.ExitCode would not propagate) and take an
/// <see cref="Trax.Cli.Machines.INodeRunner"/> so these run without spawning node.
/// </summary>
public class MachineCommandTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trax-machine-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static string ThisAssembly => typeof(DeclarativeTurnstileMachine).Assembly.Location;
    private static string TurnstileName => typeof(DeclarativeTurnstileMachine).FullName!;

    [Test]
    public void Create_builds_the_machine_command_group()
    {
        var command = MachineCommand.Create();

        command.Name.Should().Be("machine");
        command
            .Subcommands.Select(c => c.Name)
            .Should()
            .BeEquivalentTo(["new", "generate", "check", "migrate"]);

        var generate = command.Subcommands.Single(c => c.Name == "generate");
        generate
            .Options.Select(o => o.Name)
            .Should()
            .Contain(["--assembly", "--ir-out", "--twin-out", "--corpus-out", "--import-style"]);
    }

    [Test]
    public void Generate_writes_the_ir_and_returns_zero()
    {
        var irOut = Path.Combine(_tempDir, "ir");
        var exit = 99;

        var stdout = CaptureOut(() =>
            exit = MachineCommand.RunGenerate(
                ThisAssembly,
                TurnstileName,
                irOut,
                null,
                null,
                null,
                null,
                null,
                null,
                new FakeNodeRunner()
            )
        );

        exit.Should().Be(0);
        File.Exists(Path.Combine(irOut, "turnstile.ir.json")).Should().BeTrue();
        stdout.Should().Contain("Generated 1 artifact(s) for 'turnstile'.");
    }

    [Test]
    public void Generate_with_an_invalid_import_style_returns_one()
    {
        var exit = 0;
        var stderr = CaptureErr(() =>
        {
            exit = MachineCommand.RunGenerate(
                ThisAssembly,
                TurnstileName,
                Path.Combine(_tempDir, "ir"),
                null,
                null,
                null,
                "package",
                null,
                null,
                new FakeNodeRunner()
            );
        });

        exit.Should().Be(1);
        stderr.Should().Contain("--import-style");
    }

    [Test]
    public void Generate_with_a_missing_assembly_returns_one()
    {
        var exit = 0;
        var stderr = CaptureErr(() =>
        {
            exit = MachineCommand.RunGenerate(
                Path.Combine(_tempDir, "missing.dll"),
                null,
                Path.Combine(_tempDir, "ir"),
                null,
                null,
                null,
                null,
                null,
                null,
                new FakeNodeRunner()
            );
        });

        exit.Should().Be(1);
        stderr.Should().Contain("not found");
    }

    [Test]
    public void Check_returns_zero_when_the_committed_ir_is_up_to_date()
    {
        var irOut = Path.Combine(_tempDir, "ir");
        Directory.CreateDirectory(irOut);
        File.WriteAllText(
            Path.Combine(irOut, "turnstile.ir.json"),
            new DeclarativeTurnstileMachine().ExportIr() + "\n"
        );

        var exit = 99;
        var stdout = CaptureOut(() =>
            exit = MachineCommand.RunCheck(
                ThisAssembly,
                TurnstileName,
                irOut,
                null,
                null,
                null,
                null,
                null,
                null,
                new FakeNodeRunner()
            )
        );

        exit.Should().Be(0);
        stdout.Should().Contain("up to date");
    }

    [Test]
    public void Check_returns_one_and_reports_drift_when_the_committed_ir_is_stale()
    {
        var irOut = Path.Combine(_tempDir, "ir");
        Directory.CreateDirectory(irOut);
        File.WriteAllText(Path.Combine(irOut, "turnstile.ir.json"), "{ \"stale\": true }\n");

        var exit = 0;
        var stderr = CaptureErr(() =>
        {
            exit = MachineCommand.RunCheck(
                ThisAssembly,
                TurnstileName,
                irOut,
                null,
                null,
                null,
                null,
                null,
                null,
                new FakeNodeRunner()
            );
        });

        exit.Should().Be(1);
        stderr.Should().Contain("stale");
    }

    [Test]
    public void Check_with_a_missing_assembly_returns_one()
    {
        var exit = 0;
        var stderr = CaptureErr(() =>
        {
            exit = MachineCommand.RunCheck(
                Path.Combine(_tempDir, "missing.dll"),
                null,
                Path.Combine(_tempDir, "ir"),
                null,
                null,
                null,
                null,
                null,
                null,
                new FakeNodeRunner()
            );
        });

        exit.Should().Be(1);
        stderr.Should().Contain("not found");
    }

    [Test]
    public void Generate_exit_code_propagates_through_the_real_command_invocation()
    {
        // The exit code must survive Parse().Invoke() (which is what Program returns), not just the handler:
        // a bad --import-style through the real action must yield a non-zero process exit for CI to catch it.
        var command = MachineCommand.Create();
        var exit = 0;

        CaptureErr(() =>
        {
            exit = command
                .Parse([
                    "generate",
                    "--assembly",
                    ThisAssembly,
                    "--machine",
                    TurnstileName,
                    "--ir-out",
                    Path.Combine(_tempDir, "ir"),
                    "--import-style",
                    "bogus",
                ])
                .Invoke();
        });

        exit.Should().Be(1);
    }

    [Test]
    public void New_scaffolds_a_machine_file_and_returns_zero()
    {
        var exit = 99;
        var stdout = CaptureOut(() =>
            exit = MachineCommand.RunNew("checkout", _tempDir, "MyApp.Machines", false, false)
        );

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "CheckoutMachine.cs")).Should().BeTrue();
        stdout.Should().Contain("Scaffolded");
    }

    [Test]
    public void New_returns_one_when_the_file_exists_without_force()
    {
        MachineCommand.RunNew("checkout", _tempDir, "MyApp", false, false);

        var exit = 0;
        var stderr = CaptureErr(() =>
        {
            exit = MachineCommand.RunNew("checkout", _tempDir, "MyApp", false, false);
        });

        exit.Should().Be(1);
        stderr.Should().Contain("already exists");
    }

    [Test]
    public void New_through_the_command_invocation_scaffolds_a_file()
    {
        var exit = 99;
        CaptureOut(() =>
        {
            exit = MachineCommand
                .Create()
                .Parse(["new", "checkout", "--output", _tempDir])
                .Invoke();
        });

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "CheckoutMachine.cs")).Should().BeTrue();
    }

    [Test]
    public void Check_through_the_command_invocation_reports_missing_and_returns_one()
    {
        // No committed IR at --ir-out, so check reports MISSING and exits 1, through the real parser wiring
        // (no node needed: an IR-only check never shells out).
        var irOut = Path.Combine(_tempDir, "ir");
        var exit = 0;
        var stdout = CaptureOut(() =>
        {
            exit = MachineCommand
                .Create()
                .Parse([
                    "check",
                    "--assembly",
                    ThisAssembly,
                    "--machine",
                    TurnstileName,
                    "--ir-out",
                    irOut,
                ])
                .Invoke();
        });

        exit.Should().Be(1);
        stdout.Should().Contain("MISSING");
    }

    [Test]
    public void Migrate_prints_the_deferred_notice()
    {
        var command = MachineCommand.Create();
        var stdout = CaptureOut(() => command.Parse("migrate").Invoke());

        stdout.Should().Contain("deferred");
    }

    private static string CaptureOut(Action action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    private static string CaptureErr(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }
        return writer.ToString();
    }
}
