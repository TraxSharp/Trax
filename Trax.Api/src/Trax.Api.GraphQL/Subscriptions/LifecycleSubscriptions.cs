using HotChocolate;
using Trax.Api.DTOs;

namespace Trax.Api.GraphQL.Subscriptions;

/// <summary>
/// GraphQL subscription type for real-time train lifecycle events.
/// Clients connect via WebSocket at the GraphQL endpoint.
/// </summary>
public class LifecycleSubscriptions
{
    [Subscribe]
    public TrainLifecycleEvent OnTrainStarted([EventMessage] TrainLifecycleEvent e) => e;

    [Subscribe]
    public TrainLifecycleEvent OnTrainCompleted([EventMessage] TrainLifecycleEvent e) => e;

    [Subscribe]
    public TrainLifecycleEvent OnTrainFailed([EventMessage] TrainLifecycleEvent e) => e;

    [Subscribe]
    public TrainLifecycleEvent OnTrainCancelled([EventMessage] TrainLifecycleEvent e) => e;

    [Subscribe]
    public TrainLifecycleEvent OnTrainStateChanged([EventMessage] TrainLifecycleEvent e) => e;
}
