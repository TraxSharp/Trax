using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The train <see cref="WaitMachine"/> invokes. These tests never run it.</summary>
public interface IWaitTrain : IServiceTrain<WaitInput, WaitOutput>;
