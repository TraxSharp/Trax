using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>Which definition of <see cref="ReminderMachine"/> a host runs, for the tests of a deploy.</summary>
public enum ReminderShape
{
    /// <summary>Version 1: <c>Review</c> expires after <see cref="ReminderMachine.Delay"/>.</summary>
    V1,

    /// <summary>Version 2, migrated from 1, whose <c>Review</c> has no timer.</summary>
    V2WithoutTimer,

    /// <summary>Version 2, migrated from 1, whose <c>Review</c> expires after <see cref="ReminderMachine.LongerDelay"/>.</summary>
    V2LongerTimer,

    /// <summary>Version 2, migrated from 1, with the same timer as version 1.</summary>
    V2SameTimer,
}
