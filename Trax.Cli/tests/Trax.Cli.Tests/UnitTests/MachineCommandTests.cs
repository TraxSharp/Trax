using System.CommandLine;
using AwesomeAssertions;
using Trax.Cli.Commands;
using Trax.Cli.Tests.Fakes;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// The <c>trax machine</c> command surface and its exit-code contract: a bad option or a load failure exits 1,
/// a clean generate/check exits 0, and <c>check</c> exits 1 on drift (the CI gate). The handlers return the
/// exit code (Program returns InvokeAsync's result, so Environment.ExitCode would not propagate) and take an
/// <see cref="Trax.Cli.Machines.INodeRunner"/> so these run without spawning node.
/// </summary>
public class MachineCommandTests
{
    private string _tempDir = "";

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
    private static string TurnstileName => typeof(DeclarativeTurnstileMachine).FullName.NotNull();

    [Test]
    public async Task Create_builds_the_machine_command_group()
    {
        var command = await MachineCommand.CreateAsync().ConfigureAwait(false);

        command.Name.Should().Be("machine");
        command
            .Subcommands.Select(c => c.Name)
            .Should()
            .BeEquivalentTo([
                "new",
                "generate",
                "check",
                "show",
                "migrate",
                "from-gherkin",
                "run",
                "missing",
                "usage",
                "explain",
                "diagram",
            ]);

        var generate = command.Subcommands.Single(c =>
            string.Equals(c.Name, "generate", StringComparison.Ordinal)
        );
        generate
            .Options.Select(o => o.Name)
            .Should()
            .Contain(["--assembly", "--ir-out", "--twin-out", "--corpus-out", "--import-style"]);
    }

    [Test]
    public async Task Generate_writes_the_ir_and_returns_zero()
    {
        var irOut = Path.Combine(_tempDir, "ir");

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunGenerateAsync(
                        ThisAssembly,
                        TurnstileName,
                        irOut,
                        twinOut: null,
                        corpusOut: null,
                        engineSrc: null,
                        importStyle: null,
                        specifier: null,
                        toolsDir: null,
                        new FakeNodeRunner(),
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stdout = run.Stdout;

        exit.Should().Be(0);
        File.Exists(Path.Combine(irOut, "turnstile.ir.json")).Should().BeTrue();
        stdout.Should().Contain("Generated 1 artifact(s) for 'turnstile'.");
    }

    [Test]
    public async Task Generate_with_an_invalid_import_style_returns_one()
    {
        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunGenerateAsync(
                        ThisAssembly,
                        TurnstileName,
                        Path.Combine(_tempDir, "ir"),
                        twinOut: null,
                        corpusOut: null,
                        engineSrc: null,
                        "package",
                        specifier: null,
                        toolsDir: null,
                        new FakeNodeRunner(),
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stderr = run.Stderr;

        exit.Should().Be(1);
        stderr.Should().Contain("--import-style");
    }

    [Test]
    public async Task Generate_with_a_missing_assembly_returns_one()
    {
        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunGenerateAsync(
                        Path.Combine(_tempDir, "missing.dll"),
                        machineName: null,
                        Path.Combine(_tempDir, "ir"),
                        twinOut: null,
                        corpusOut: null,
                        engineSrc: null,
                        importStyle: null,
                        specifier: null,
                        toolsDir: null,
                        new FakeNodeRunner(),
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stderr = run.Stderr;

        exit.Should().Be(1);
        stderr.Should().Contain("not found");
    }

    [Test]
    public async Task Check_returns_zero_when_the_committed_ir_is_up_to_date()
    {
        var irOut = Path.Combine(_tempDir, "ir");
        Directory.CreateDirectory(irOut);
        await File.WriteAllTextAsync(
            Path.Combine(irOut, "turnstile.ir.json"),
            new DeclarativeTurnstileMachine().ExportIr() + "\n"
        );

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunCheckAsync(
                        ThisAssembly,
                        TurnstileName,
                        irOut,
                        twinOut: null,
                        corpusOut: null,
                        engineSrc: null,
                        importStyle: null,
                        specifier: null,
                        toolsDir: null,
                        new FakeNodeRunner(),
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stdout = run.Stdout;

        exit.Should().Be(0);
        stdout.Should().Contain("up to date");
    }

    [Test]
    public async Task Check_returns_one_and_reports_drift_when_the_committed_ir_is_stale()
    {
        var irOut = Path.Combine(_tempDir, "ir");
        Directory.CreateDirectory(irOut);
        await File.WriteAllTextAsync(
            Path.Combine(irOut, "turnstile.ir.json"),
            "{ \"stale\": true }\n"
        );

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunCheckAsync(
                        ThisAssembly,
                        TurnstileName,
                        irOut,
                        twinOut: null,
                        corpusOut: null,
                        engineSrc: null,
                        importStyle: null,
                        specifier: null,
                        toolsDir: null,
                        new FakeNodeRunner(),
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stderr = run.Stderr;

        exit.Should().Be(1);
        stderr.Should().Contain("stale");
    }

    [Test]
    public async Task Check_with_a_missing_assembly_returns_one()
    {
        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunCheckAsync(
                        Path.Combine(_tempDir, "missing.dll"),
                        machineName: null,
                        Path.Combine(_tempDir, "ir"),
                        twinOut: null,
                        corpusOut: null,
                        engineSrc: null,
                        importStyle: null,
                        specifier: null,
                        toolsDir: null,
                        new FakeNodeRunner(),
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stderr = run.Stderr;

        exit.Should().Be(1);
        stderr.Should().Contain("not found");
    }

    [Test]
    public async Task Generate_exit_code_propagates_through_the_real_command_invocation()
    {
        // The exit code must survive Parse().Invoke() (which is what Program returns), not just the handler:
        // a bad --import-style through the real action must yield a non-zero process exit for CI to catch it.
        var command = await MachineCommand.CreateAsync().ConfigureAwait(false);

        var exit = (
            await CommandOutput
                .InvokeAsync(
                    command.Parse([
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
                )
                .ConfigureAwait(false)
        ).ExitCode;

        exit.Should().Be(1);
    }

    [Test]
    public async Task New_scaffolds_a_machine_file_and_returns_zero()
    {
        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunNewAsync(
                        "checkout",
                        _tempDir,
                        "MyApp.Machines",
                        withEffect: false,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stdout = run.Stdout;

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "CheckoutMachine.cs")).Should().BeTrue();
        stdout.Should().Contain("Scaffolded");
    }

    [Test]
    public async Task New_names_the_package_and_the_prerequisites_of_generate()
    {
        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunNewAsync(
                        "checkout",
                        _tempDir,
                        "MyApp.Machines",
                        withEffect: false,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var stdout = run.Stdout;

        stdout.Should().Contain("dotnet add package Trax.StateMachine.Persistence --version ");
        stdout.Should().Contain("--assembly").And.Contain("Node.js 22").And.Contain("--engine-src");
    }

    [Test]
    public async Task New_returns_one_when_the_file_exists_without_force()
    {
        await MachineCommand
            .RunNewAsync(
                "checkout",
                _tempDir,
                "MyApp",
                withEffect: false,
                force: false,
                TextWriter.Null,
                TextWriter.Null
            )
            .ConfigureAwait(false);

        var run = await CommandOutput
            .OfAsync(
                (o, e) =>
                    MachineCommand.RunNewAsync(
                        "checkout",
                        _tempDir,
                        "MyApp",
                        withEffect: false,
                        force: false,
                        o,
                        e
                    )
            )
            .ConfigureAwait(false);
        var exit = run.ExitCode;
        var stderr = run.Stderr;

        exit.Should().Be(1);
        stderr.Should().Contain("already exists");
    }

    [Test]
    public async Task New_through_the_command_invocation_scaffolds_a_file()
    {
        var exit = (
            await CommandOutput
                .InvokeAsync(
                    (await MachineCommand.CreateAsync().ConfigureAwait(false)).Parse([
                        "new",
                        "checkout",
                        "--output",
                        _tempDir,
                    ])
                )
                .ConfigureAwait(false)
        ).ExitCode;

        exit.Should().Be(0);
        File.Exists(Path.Combine(_tempDir, "CheckoutMachine.cs")).Should().BeTrue();
    }

    [Test]
    public async Task Check_through_the_command_invocation_reports_missing_and_returns_one()
    {
        // No committed IR at --ir-out, so check reports MISSING and exits 1, through the real parser wiring
        // (no node needed: an IR-only check never shells out).
        var irOut = Path.Combine(_tempDir, "ir");
        var run = await CommandOutput
            .InvokeAsync(
                (await MachineCommand.CreateAsync().ConfigureAwait(false)).Parse([
                    "check",
                    "--assembly",
                    ThisAssembly,
                    "--machine",
                    TurnstileName,
                    "--ir-out",
                    irOut,
                ])
            )
            .ConfigureAwait(false);
        var (exit, stdout) = (run.ExitCode, run.Stdout);

        exit.Should().Be(1);
        stdout.Should().Contain("MISSING");
    }

    [Test]
    public async Task Migrate_fails_with_the_deferred_notice_on_stderr()
    {
        // It does nothing, so a CI step that runs it must not report success.
        var command = await MachineCommand.CreateAsync().ConfigureAwait(false);
        var run = await CommandOutput.InvokeAsync(command.Parse("migrate")).ConfigureAwait(false);
        var (exit, stderr) = (run.ExitCode, run.Stderr);

        exit.Should().Be(1);
        stderr.Should().Contain("not implemented");
    }

    [Test]
    public async Task RunMigrate_returns_non_zero()
    {
        (await MachineCommand.RunMigrateAsync(TextWriter.Null).ConfigureAwait(false))
            .Should()
            .NotBe(0);
    }

    [Test]
    public async Task RunMigrate_speaks_without_internal_decision_names()
    {
        var run = await CommandOutput
            .OfAsync((o, e) => MachineCommand.RunMigrateAsync(e))
            .ConfigureAwait(false);
        var stderr = run.Stderr;

        stderr.Should().NotContain("Decision");
    }
}
