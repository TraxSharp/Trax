using Trax.Mediator.Testing;

namespace Trax.Mediator.Testing.Tests;

/// <summary>A train interface not named after its class, which the registry accepts.</summary>
public interface IRenamedContract : IServiceTrain<string, int>;
