using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Gherkin;
using Trax.StateMachine.Persistence.FromIr;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>The train, named by its interface; deriving and serving the machine never runs it.</summary>
public interface IAimTrain : IServiceTrain<AimInput, AimOutput>;
