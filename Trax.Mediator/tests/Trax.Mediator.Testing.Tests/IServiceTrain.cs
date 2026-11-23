using Trax.Mediator.Testing;

namespace Trax.Mediator.Testing.Tests;

// Test-local stand-ins matching the Trax base type names ("ServiceTrain`2" / "IServiceTrain`2"),
// which the guard matches by name. Used to exercise the reflection guard without the real framework.
public interface IServiceTrain<TIn, TOut>;
