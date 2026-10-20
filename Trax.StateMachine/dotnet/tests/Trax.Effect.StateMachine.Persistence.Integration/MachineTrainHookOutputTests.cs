using System.Reflection;
using AwesomeAssertions;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Every train a machine operation runs through withholds its output, an instance's snapshot, from the lifecycle
/// hooks, so the broadcaster and SignalR never carry a context.
/// </summary>
[Property("adr", Adr)]
public class MachineTrainHookOutputTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public void Every_machine_train_withholds_its_output_from_the_lifecycle_hooks()
    {
        var trains = typeof(ISaveSnapshot)
            .Assembly.GetTypes()
            .Where(t =>
                t is { IsClass: true, IsAbstract: false }
                && t.Namespace == typeof(ISaveSnapshot).Namespace
                && t.GetInterfaces()
                    .Any(i =>
                        i.IsGenericType
                        && i.GetGenericTypeDefinition()
                            == typeof(Trax.Effect.Services.ServiceTrain.IServiceTrain<,>)
                    )
            )
            .ToList();

        trains.Should().HaveCount(6, "save, advance, load, send, start and discard");
        trains
            .Should()
            .AllSatisfy(t =>
                t.GetCustomAttribute<OutputWithheldFromLifecycleHooksAttribute>()
                    .Should()
                    .NotBeNull($"{t.Name}'s output is a snapshot, never published. See {Adr}")
            );
    }
}
