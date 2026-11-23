using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.StateMachine.Persistence.Integration.Fakes;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

/// <summary>What a <see cref="ControlledCharge"/> does on one run.</summary>
public enum ChargeRun
{
    /// <summary>Returns a fresh receipt at once.</summary>
    Succeed,

    /// <summary>Waits until <see cref="ControlledCharge.Release"/>, then returns a fresh receipt.</summary>
    Hold,

    /// <summary>Waits until <see cref="ControlledCharge.Release"/>, then throws: the charge did not happen.</summary>
    HoldThenFail,

    /// <summary>Throws at once: the charge did not happen.</summary>
    Fail,

    /// <summary>Throws a cancellation at once: whether the charge happened is unknown.</summary>
    TimeOut,
}
