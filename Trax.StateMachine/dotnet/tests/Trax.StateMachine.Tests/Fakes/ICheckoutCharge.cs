using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Tests.Fakes;

/// <summary>A marker for the checkout's one irreversible effect (resolved from DI in a real host).</summary>
public interface ICheckoutCharge;
