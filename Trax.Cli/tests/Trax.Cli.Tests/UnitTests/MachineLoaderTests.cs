using FluentAssertions;
using Trax.Cli.Machines;
using Trax.Cli.Tests.Fakes;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="MachineLoader"/> turns a compiled assembly into the <see cref="IMachine"/> the CLI exports. These
/// pin discovery (only real machines, sorted), selection (single, named, ambiguous, unknown), and the file
/// errors, since a wrong pick or a silent mis-load would generate the wrong machine's artifacts.
/// </summary>
public class MachineLoaderTests
{
    private static string ThisAssemblyPath => typeof(DeclarativeTurnstileMachine).Assembly.Location;

    private static readonly IReadOnlyList<Type> Two =
    [
        typeof(DeclarativeTurnstileMachine),
        typeof(SecondTurnstileMachine),
    ];

    [Test]
    public void DiscoverMachines_finds_the_declarative_machines_in_this_assembly()
    {
        var machines = MachineLoader.DiscoverMachines(typeof(DeclarativeTurnstileMachine).Assembly);

        machines.Should().Contain(typeof(DeclarativeTurnstileMachine));
        machines.Should().Contain(typeof(SecondTurnstileMachine));
        // Abstract Machine<,> itself and non-machine types are excluded.
        machines.Should().OnlyContain(t => typeof(IMachine).IsAssignableFrom(t) && !t.IsAbstract);
    }

    [Test]
    public void SelectMachine_returns_the_only_machine_when_none_named()
    {
        MachineLoader
            .SelectMachine([typeof(DeclarativeTurnstileMachine)], null, "asm.dll")
            .Should()
            .Be(typeof(DeclarativeTurnstileMachine));
    }

    [Test]
    public void SelectMachine_returns_the_named_machine()
    {
        MachineLoader
            .SelectMachine(Two, typeof(SecondTurnstileMachine).FullName, "asm.dll")
            .Should()
            .Be(typeof(SecondTurnstileMachine));
    }

    [Test]
    public void SelectMachine_throws_listing_candidates_when_ambiguous()
    {
        var select = () => MachineLoader.SelectMachine(Two, null, "asm.dll");

        select
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*2 machines*--machine*")
            .WithMessage($"*{typeof(DeclarativeTurnstileMachine).FullName}*");
    }

    [Test]
    public void SelectMachine_throws_when_the_named_machine_is_absent()
    {
        var select = () => MachineLoader.SelectMachine(Two, "Nope.NotAMachine", "asm.dll");

        select
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Nope.NotAMachine*not found*");
    }

    [Test]
    public void Load_throws_a_clear_error_when_the_assembly_is_missing()
    {
        var load = () => MachineLoader.Load("/no/such/assembly.dll", null);

        load.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Test]
    public void Load_instantiates_the_named_machine_from_a_real_assembly()
    {
        var machine = MachineLoader.Load(
            ThisAssemblyPath,
            typeof(DeclarativeTurnstileMachine).FullName
        );

        machine.Name.Should().Be("turnstile");
        // The loaded machine unifies with the CLI's IMachine, so ExportIr() is reachable and returns the IR.
        machine.ExportIr().Should().Contain("\"id\":\"turnstile\"");
    }

    [Test]
    public void Load_throws_when_the_assembly_has_multiple_machines_and_none_named()
    {
        var load = () => MachineLoader.Load(ThisAssemblyPath, null);

        load.Should().Throw<InvalidOperationException>().WithMessage("*--machine*");
    }

    [Test]
    public void Load_reports_a_clear_error_for_a_file_that_is_not_a_dotnet_assembly()
    {
        var fake = Path.Combine(Path.GetTempPath(), $"trax-not-an-assembly-{Guid.NewGuid():N}.dll");
        File.WriteAllText(fake, "this is not a managed assembly");
        try
        {
            var load = () => MachineLoader.Load(fake, null);

            load.Should().Throw<InvalidOperationException>().WithMessage("*as a .NET assembly*");
        }
        finally
        {
            File.Delete(fake);
        }
    }

    [Test]
    public void Load_throws_when_the_assembly_contains_no_machines()
    {
        // The CLI's own assembly has the command code but no IMachine implementations. This is also the shape
        // of the version-mismatch case (a machine built against a different engine is not recognized).
        var load = () => MachineLoader.Load(typeof(MachineLoader).Assembly.Location, null);

        load.Should().Throw<InvalidOperationException>().WithMessage("*No machines found*");
    }
}
