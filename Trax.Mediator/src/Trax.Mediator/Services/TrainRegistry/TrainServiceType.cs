using System;
using System.Linq;
using Trax.Core.Exceptions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.TrainRegistry;

/// <summary>
/// Chooses the service type a scanned train is registered under. The registry and the public
/// <c>RegisterServiceTrains</c> both call it, so the two cannot choose differently.
/// </summary>
internal static class TrainServiceType
{
    private static readonly Type ServiceTrainDefinition = typeof(IServiceTrain<,>);

    /// <summary>
    /// Returns the train's own interface: the non-generic interface that derives from
    /// <c>IServiceTrain&lt;TIn, TOut&gt;</c>, which is also the train's canonical name. Without one,
    /// returns the closed <c>IServiceTrain&lt;TIn, TOut&gt;</c> the class implements.
    /// </summary>
    /// <remarks>
    /// Only an interface that is itself a train interface qualifies. Any other non-generic
    /// interface, such as a marker or <see cref="IDisposable"/> on a shared base class, is not the
    /// train's service type, whatever order <see cref="Type.GetInterfaces"/> lists it in. When one
    /// train interface extends another, the most derived one is chosen.
    /// </remarks>
    /// <param name="concreteType">A concrete class implementing <c>IServiceTrain&lt;,&gt;</c>.</param>
    /// <exception cref="TrainException">
    /// The class does not implement <c>IServiceTrain&lt;,&gt;</c>, or it implements two train
    /// interfaces neither of which extends the other, so it has no single canonical name.
    /// </exception>
    internal static Type Select(Type concreteType)
    {
        var interfaces = concreteType.GetInterfaces();

        var candidates = interfaces
            .Where(i => !i.IsGenericType && IsServiceTrainInterface(i))
            .ToList();

        // Drop an interface another candidate extends: ITrain : IBaseTrain selects ITrain.
        var mostDerived = candidates
            .Where(c => !candidates.Any(other => other != c && c.IsAssignableFrom(other)))
            .ToList();

        if (mostDerived.Count == 1)
            return mostDerived[0];

        if (mostDerived.Count > 1)
            throw new TrainException(
                $"Train ({concreteType.FullName}) implements more than one train interface "
                    + $"({string.Join(", ", mostDerived.Select(i => i.FullName))}), so it has no "
                    + "single canonical name. Give it one interface deriving from IServiceTrain<TIn, TOut>, "
                    + "or have one of them extend the other."
            );

        return interfaces.FirstOrDefault(IsClosedServiceTrain)
            ?? throw new TrainException(
                $"Could not find an interface attached to ({concreteType.Name}) with Full Name ({concreteType.FullName}) on Assembly ({concreteType.AssemblyQualifiedName}). At least one Interface is required."
            );
    }

    /// <summary>
    /// The closed <c>IServiceTrain&lt;TIn, TOut&gt;</c> that <paramref name="type"/> is or
    /// implements, or null when it is not a train type.
    /// </summary>
    internal static Type? FindClosedServiceTrain(Type type) =>
        IsClosedServiceTrain(type)
            ? type
            : type.GetInterfaces().FirstOrDefault(IsClosedServiceTrain);

    /// <summary>
    /// Whether an interface <paramref name="type"/> implements derives from a closed
    /// <c>IServiceTrain&lt;,&gt;</c>. Only called on what <see cref="Type.GetInterfaces"/> returns.
    /// </summary>
    private static bool IsServiceTrainInterface(Type type) =>
        type.GetInterfaces().Any(IsClosedServiceTrain);

    private static bool IsClosedServiceTrain(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == ServiceTrainDefinition;
}
