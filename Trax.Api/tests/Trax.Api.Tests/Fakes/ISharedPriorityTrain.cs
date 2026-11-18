using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>The service a query train taking <see cref="SharedPriorityInput"/> is registered under.</summary>
public interface ISharedPriorityTrain;
