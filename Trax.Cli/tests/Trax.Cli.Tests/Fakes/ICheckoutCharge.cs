using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

/// <summary>A marker for the checkout's one irreversible effect (resolved from DI in a real host).</summary>
public interface ICheckoutCharge : ISnapshotEffect;
