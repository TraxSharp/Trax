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

/// <summary>
/// The architecture guards for the Bookworm flagship, run entirely by subclassing the framework
/// guard fixtures and supplying configuration.
///
/// <para>
/// There are no test bodies here: the [Test] methods live in the packages
/// (Trax.Effect.Data.Testing / Trax.Api.GraphQL.Testing / Trax.Mediator.Testing) and are discovered
/// through these subclasses. Each package subclasses its own fixture in a self-test too, but does it
/// inside the repo that ships the fixture, against that repo's project references. This is the only
/// place the fixtures are adopted across a real PackageReference, so a type left internal or a file
/// left out of the pack fails here and nowhere else.
/// </para>
///
/// <para>Enforces <c>docs/adr/0002-the-samples-adopt-the-guards-as-a-consumer-would.md</c>.</para>
/// </summary>
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
