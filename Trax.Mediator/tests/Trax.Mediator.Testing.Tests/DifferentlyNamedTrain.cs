using Trax.Mediator.Testing;

namespace Trax.Mediator.Testing.Tests;

public sealed class DifferentlyNamedTrain : ServiceTrain<string, int>, IRenamedContract;
