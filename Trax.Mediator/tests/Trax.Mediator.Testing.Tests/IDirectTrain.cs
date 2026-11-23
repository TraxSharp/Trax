using Trax.Mediator.Testing;

namespace Trax.Mediator.Testing.Tests;

/// <summary>A train that implements its interface directly, with no ServiceTrain base.</summary>
public interface IDirectTrain : IServiceTrain<long, int>;
