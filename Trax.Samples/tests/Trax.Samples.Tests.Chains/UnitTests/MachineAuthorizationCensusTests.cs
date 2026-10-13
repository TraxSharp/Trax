using System.Reflection;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Samples.Tests.Chains.UnitTests;

/// <summary>
/// Every machine a sample host registers, described by <see cref="MachineCensus"/>: each client event and each
/// read names the rule that guards it. A host refuses to start with an event nobody declared a rule for, so a
/// sample that would not start is caught here, without its database.
/// </summary>
/// <remarks>
/// The machines are found the way <c>AddStateMachines</c> finds them, by scanning the sample assemblies this
/// project references for concrete machines with a parameterless constructor. Building one runs only its
/// declaration.
/// </remarks>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
[TestFixture]
public class MachineAuthorizationCensusTests
{
    // The machines the sample hosts register today; a scan that finds fewer is looking in the wrong place.
    private static readonly string[] KnownMachines =
    [
        "topic-map",
        "source-partition",
        "turnstile",
        "checkout",
    ];

    [Test]
    public void Every_exposed_machines_events_and_reads_declare_a_rule()
    {
        var machines = SampleMachines();
        machines.Select(m => m.Name).Should().Contain(KnownMachines);

        var census = MachineCensus.Describe(machines);

        var undeclared = census
            .Where(e => e.Kind is nameof(TriggerKind.Client) or "Read")
            .Where(e =>
                string.IsNullOrWhiteSpace(e.Rule)
                || e.Rule.Equals("none", StringComparison.OrdinalIgnoreCase)
                || e.Rule.Equals("undeclared", StringComparison.OrdinalIgnoreCase)
            )
            .Select(e => $"{e.Machine}.{e.Name} ({e.Kind})")
            .ToList();

        census.Should().NotBeEmpty();
        undeclared
            .Should()
            .BeEmpty(
                "every client event and read declares Requires, RequiresPolicy, AnyAuthenticated or "
                    + "Anonymous, and there is no machine-level default (Trax.Docs/adr/0048)"
            );
    }

    private static List<IMachine> SampleMachines() =>
        Directory
            .EnumerateFiles(AppContext.BaseDirectory, "Trax.Samples.*.dll")
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path)))
            .SelectMany(a => a.GetTypes())
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && typeof(IMachine).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
            )
            .Select(t => (IMachine)Activator.CreateInstance(t)!)
            .ToList();
}
