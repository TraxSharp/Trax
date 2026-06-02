using System.Reflection;
using NUnit.Framework;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Api.GraphQL.Testing;
using Trax.Core.Testing;
using Trax.Effect.Data.Testing;
using Trax.Mediator.Testing;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.CrossSchema;
using Trax.Samples.Bookworm.Lending.Context;

namespace Trax.Samples.Tests.Reflection;

// The architecture guards for the Bookworm flagship are run entirely by subclassing the framework
// guard fixtures and supplying configuration. There are no test bodies here: the [Test] methods live
// in the packages (Trax.Effect.Data.Testing / Trax.Api.GraphQL.Testing / Trax.Mediator.Testing) and
// are discovered through these subclasses. This is exactly how any consumer adopts the guards.

[TestFixture]
public sealed class BookwormDataLayerGuards : DomainDataLayerGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

    protected override IReadOnlyList<Type> DomainContexts =>
        [typeof(CatalogDbContext), typeof(LendingDbContext)];
}

[TestFixture]
public sealed class BookwormCrossSchemaGuards : CrossSchemaGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

    protected override IReadOnlyList<CrossSchemaEdge> Edges => CrossSchemaEdges.All;
}

[TestFixture]
public sealed class BookwormTrainGuards : TrainGuardFixture
{
    protected override IReadOnlyList<Assembly> TrainAssemblies =>
        [typeof(Trax.Samples.Bookworm.AssemblyMarker).Assembly];
}
