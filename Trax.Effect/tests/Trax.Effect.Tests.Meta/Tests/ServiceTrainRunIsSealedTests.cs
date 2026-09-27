using System.Reflection;
using FluentAssertions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Meta.Tests;

/// <summary>
/// A service train does its work in <c>Junctions()</c> and nowhere else, so no <c>Run</c> on
/// <see cref="ServiceTrain{TIn,TOut}"/> can be overridden. An override bypasses the metadata row,
/// the lifecycle hooks and the outcome write that <c>Run</c> owns. <c>NewMonad</c>, which builds
/// the monad the chain runs on, is closed the same way.
///
/// <para>Enforces <c>docs/adr/0009-a-service-train-does-its-work-in-junctions.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0009-a-service-train-does-its-work-in-junctions.md")]
[TestFixture]
public class ServiceTrainRunIsSealedTests
{
    private const string Adr = "docs/adr/0009-a-service-train-does-its-work-in-junctions.md";

    private static IEnumerable<MethodInfo> RunOverloads() =>
        typeof(ServiceTrain<,>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name == "Run" && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly));

    [Test]
    public void Every_Run_a_service_train_inherits_or_declares_is_closed_to_overriding()
    {
        var overloads = RunOverloads().ToList();
        overloads
            .Should()
            .HaveCountGreaterThanOrEqualTo(
                3,
                "Run(input, ct), Run(input, metadata), and Run(input, metadata, ct)"
            );

        var open = overloads
            .Where(m => m.IsVirtual && !m.IsFinal)
            .Select(m =>
                $"Run({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))}) "
                + $"declared on {m.DeclaringType!.Name}"
            )
            .ToList();

        open.Should()
            .BeEmpty(
                $"{Adr}: a service train does its work in Junctions(), and an overridable Run is a "
                    + "way around the metadata row, the lifecycle hooks and the outcome write"
            );
    }

    [Test]
    public void The_cancellation_token_overload_is_a_sealed_override()
    {
        var run = typeof(ServiceTrain<,>)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Single(m =>
                m.Name == "Run"
                && m.GetParameters() is [_, { ParameterType: var second }]
                && second == typeof(CancellationToken)
            );

        run.IsFinal.Should()
            .BeTrue(
                $"{Adr}: it overrides Train.Run, which stays virtual, so sealing it here is what "
                    + "stops a service train overriding it"
            );
    }

    [Test]
    public void NewMonad_is_a_sealed_override()
    {
        var newMonad = typeof(ServiceTrain<,>).GetMethod(
            "NewMonad",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
        );

        newMonad.Should().NotBeNull();
        newMonad!
            .IsFamily.Should()
            .BeTrue("Core declares it protected, and an override keeps that");
        newMonad
            .IsFinal.Should()
            .BeTrue(
                $"{Adr}: it hands the chain its monad, so a service train that replaced it would "
                    + "run Junctions() on something Run does not control"
            );
    }
}
