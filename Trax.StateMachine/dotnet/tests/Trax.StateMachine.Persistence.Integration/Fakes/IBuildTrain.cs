using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The train <see cref="BuildMachine"/> invokes, named by its interface. These tests never run it.</summary>
public interface IBuildTrain : IServiceTrain<BuildInput, BuildOutput>;
