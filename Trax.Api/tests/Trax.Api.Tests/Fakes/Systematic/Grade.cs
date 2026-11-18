using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Systematic.Orders;

/// <summary>A consumer namespace that starts with <c>System</c> without being it.</summary>
public enum Grade
{
    Low,
    VeryHigh,
}
