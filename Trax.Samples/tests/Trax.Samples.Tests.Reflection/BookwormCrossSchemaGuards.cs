using NUnit.Framework;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Api.GraphQL.Testing;
using Trax.Core.Testing;
using Trax.Samples.Bookworm.CrossSchema;

namespace Trax.Samples.Tests.Reflection;

[TestFixture]
public sealed class BookwormCrossSchemaGuards : CrossSchemaGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["samples"] };

    protected override IReadOnlyList<CrossSchemaEdge> Edges => CrossSchemaEdges.All;
}
