using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>Where a <see cref="ObservedLauncher"/> fails the write it is part of.</summary>
public enum LaunchFault
{
    None,

    /// <summary>After the snapshot is written, before the run's entry is.</summary>
    BeforeEnqueue,

    /// <summary>After the run's entry is written, before the transaction commits.</summary>
    AfterEnqueue,

    /// <summary>Before the run's entry is written, as an authorization the caller fails.</summary>
    Forbidden,
}
