using System.Reflection;
using NUnit.Framework;
using Trax.Mediator.Testing;

namespace Trax.Samples.Tests.Reflection;

[TestFixture]
public sealed class BookwormTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies =>
        [typeof(Trax.Samples.Bookworm.AssemblyMarker).Assembly];
}
