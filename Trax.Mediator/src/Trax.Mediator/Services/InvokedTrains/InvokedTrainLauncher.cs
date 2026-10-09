using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Monad;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.StateMachine.Persistence;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;

namespace Trax.Mediator.Services.InvokedTrains;

/// <summary>
/// The mediator's <see cref="IInvokedTrainLauncher"/>, registered scoped by <c>AddMediator</c>. It queues a state's
/// invoked train through <see cref="TrainExecutionService"/>, so the run is authorized and written as any caller's
/// enqueue is, into the caller's transaction; and it tells the startup check what about a train stops a machine from
/// invoking it.
/// </summary>
/// <remarks>
/// A run the machine invokes must be cancellable from any host, because leaving the state cancels it through the
/// run's database cancel flag, and only an effect junction reads that flag between junctions. So every step of the
/// train's chain, inside a routing step's tracks and a <c>Parallel</c> step's branches too, is an
/// <see cref="EffectJunction{TIn,TOut}"/>, and the train is a <see cref="ServiceTrain{TIn,TOut}"/>, whose run cannot
/// be overridden to skip them.
/// </remarks>
internal sealed class InvokedTrainLauncher(
    ITrainDiscoveryService discovery,
    IServiceProvider services
) : IInvokedTrainLauncher
{
    /// <inheritdoc/>
    public async Task Launch(
        InvokedTrainLaunch launch,
        IDataContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (
            services.GetRequiredService<ITrainExecutionService>()
            is not TrainExecutionService execution
        )
            throw new InvalidOperationException(
                "A state's invoked train is queued through the mediator's ITrainExecutionService, and the one "
                    + "registered here replaces it. Remove the replacement, or invoke no trains from machines."
            );

        await execution.QueueInvokedAsync(launch, context, cancellationToken);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Refusals(
        InvokedTrainDeclaration declaration,
        IServiceProvider services
    )
    {
        var at = InvokeRefusals.At(declaration.Machine, declaration.State, declaration.TrainType);

        // Launch queues through the mediator's own execution service, inside the caller's transaction; a host that
        // replaced it would fail at the first entry, so it is refused here instead.
        if (services.GetService<ITrainExecutionService>() is not TrainExecutionService)
            return
            [
                $"{at}, but the ITrainExecutionService registered here replaces the mediator's, and a state's "
                    + "invoked train is queued through the mediator's own. Remove the replacement, or invoke no "
                    + "trains from machines.",
            ];

        var registration = discovery
            .DiscoverTrains()
            .FirstOrDefault(r => r.ServiceType == declaration.TrainType);
        if (registration is null)
            return
            [
                $"{at}, which is not a registered train. Register it with AddMediator(...) so the run can be "
                    + "queued and dispatched.",
            ];

        var problems = new List<string>();
        var implementation = registration.ImplementationType;
        var train = implementation.Name;

        if (!DerivesFrom(implementation, typeof(ServiceTrain<,>)))
            problems.Add(
                $"{at}, whose class {train} is not a ServiceTrain. Only a ServiceTrain's run is sealed to its "
                    + "junctions, so only it can be cancelled when the state is left."
            );

        if (QueueMemberOverrides.OnQueue(implementation) is not null)
            problems.Add(
                $"{at}, whose class {train} overrides OnQueue. A queue hook commits on its own, outside the "
                    + "transaction that enters the state, so an invoked train may not have one."
            );
        if (Overrides(implementation, "DeferQueuePromotion"))
            problems.Add(
                $"{at}, whose class {train} overrides DeferQueuePromotion. A deferred entry commits on its own, "
                    + "outside the transaction that enters the state, so an invoked train may not defer it."
            );

        if (declaration.SystemOwned)
        {
            if (registration.HasAuthorizeAttribute)
                problems.Add(
                    $"{at}, which declares [TraxAuthorize]. A system-owned machine's train runs in the trusted "
                        + "execution scope, as a scheduled manifest run does, where user requirements are not "
                        + "checked; invoke only a train with none."
                );
        }
        else
        {
            if (registration.RequiredPolicies.Count > 0 || registration.RequiredRoleSets.Count > 0)
                problems.Add(
                    $"{at}, whose [TraxAuthorize] names "
                        + Requirements(registration)
                        + ", stricter than the machine's own mutations, which need only an authenticated "
                        + "user. Entering the state would be a way around it; invoke it from a system-owned "
                        + "machine or relax it."
                );
            if (registration.IsBroadcastEnabled)
                problems.Add(InvokeRefusals.Broadcast(at));
        }

        problems.AddRange(ChainProblems(at, registration, services));
        return problems;
    }

    private static string Requirements(TrainRegistration registration)
    {
        var parts = new List<string>();
        if (registration.RequiredPolicies.Count > 0)
            parts.Add("policy " + string.Join(", ", registration.RequiredPolicies));
        if (registration.RequiredRoles.Count > 0)
            parts.Add("roles " + string.Join(", ", registration.RequiredRoles));
        return string.Join(" and ", parts);
    }

    // Reads the train's declared chain and refuses every step a cross-host cancel cannot stop.
    private static IEnumerable<string> ChainProblems(
        string at,
        TrainRegistration registration,
        IServiceProvider services
    )
    {
        ChainRecorder chain;
        try
        {
            var train = services.GetRequiredService(registration.ServiceType);
            var declared =
                train
                    .GetType()
                    .GetMethod(nameof(Core.Train.Train<,>.DeclaredChain), Type.EmptyTypes)
                ?? throw new InvalidOperationException(
                    $"{train.GetType().Name} does not derive from Train<TIn, TOut>"
                );
            chain = (ChainRecorder)declared.Invoke(train, null)!;
        }
        catch (Exception ex)
        {
            var cause = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return
            [
                $"{at}, whose chain could not be read outside a request ({cause.Message}), so it cannot be "
                    + "checked to be cancellable.",
            ];
        }

        var collection = services.GetService<IServiceCollection>();
        var problems = new List<string>();
        Walk(chain, "", problems);
        return problems;

        void Walk(ChainRecorder recorder, string where, List<string> into)
        {
            foreach (var refusal in recorder.Refusals)
                into.Add($"{at}, whose chain{where} is refused: {refusal}");

            for (var i = 0; i < recorder.Steps.Count; i++)
            {
                var step = recorder.Steps[i];
                switch (step.Kind)
                {
                    case ChainStepKind.Chain
                    or ChainStepKind.ShortCircuit
                        when step.Junction is { } junction
                            && !DerivesFrom(junction, typeof(EffectJunction<,>)):
                        into.Add(
                            $"{at}, whose chain{where} runs the plain junction {junction.Name} at step "
                                + $"{i + 1}. Only an EffectJunction reads the run's cancel flag, so leaving the "
                                + $"state could not stop the run on another host. Derive {junction.Name} from "
                                + "EffectJunction."
                        );
                        break;
                    case ChainStepKind.IChain when step.Junction is { } contract:
                        if (EffectImplementation(collection, contract) is { } why)
                            into.Add(
                                $"{at}, whose chain{where} runs IChain<{contract.Name}> at step {i + 1}, {why} "
                                    + "Only an EffectJunction reads the run's cancel flag, so leaving the state "
                                    + "could not stop the run on another host."
                            );
                        break;
                }

                foreach (var track in recorder.TracksAt(i))
                    Walk(track.Steps, $"{where} ({step.Kind} step {i + 1}, '{track.Name}')", into);
            }
        }
    }

    // Why the junction IChain resolves for this interface is not known to be an effect junction, or null when the
    // container registers it with a class that is one.
    private static string? EffectImplementation(IServiceCollection? collection, Type contract)
    {
        var registered = collection?.LastOrDefault(d => d.ServiceType == contract);
        if (registered is null)
            return "which the container does not register, so the junction it runs cannot be checked.";
        var implementation = registered.IsKeyedService
            ? registered.KeyedImplementationType
            : registered.ImplementationType;
        if (implementation is null)
            return "which the container builds with a factory, so the junction it runs cannot be checked. "
                + "Register the interface with its class.";
        return DerivesFrom(implementation, typeof(EffectJunction<,>))
            ? null
            : $"whose registered class {implementation.Name} is not an EffectJunction.";
    }

    private static bool DerivesFrom(Type type, Type genericBase)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.IsGenericType && current.GetGenericTypeDefinition() == genericBase)
                return true;
        return false;
    }

    private static bool Overrides(Type implementation, string property)
    {
        var getter = implementation
            .GetProperty(
                property,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetMethod;
        var declaring = getter?.DeclaringType;
        if (declaring is { IsGenericType: true })
            declaring = declaring.GetGenericTypeDefinition();
        return getter is not null && declaring != typeof(ServiceTrain<,>);
    }
}
