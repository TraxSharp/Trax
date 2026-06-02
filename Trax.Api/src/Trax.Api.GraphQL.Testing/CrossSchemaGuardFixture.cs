using NUnit.Framework;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

// The [Test] method names are the documentation; XML doc comments on them would be pure redundancy.
#pragma warning disable CS1591

namespace Trax.Api.GraphQL.Testing;

/// <summary>
/// Pre-written cross-schema GraphQL guards. A consumer subclasses this, supplies its edge manifest
/// (and <see cref="Options"/> if the source scan roots differ), and runs <c>dotnet test</c>. No test
/// bodies to write.
/// </summary>
/// <remarks>
/// Example:
/// <code>
/// [TestFixture]
/// public sealed class MyCrossSchemaGuards : CrossSchemaGuardFixture
/// {
///     protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs"] };
///     protected override IReadOnlyList&lt;CrossSchemaEdge&gt; Edges => MyCrossSchemaEdges.All;
/// }
/// </code>
/// </remarks>
[TestFixture]
public abstract class CrossSchemaGuardFixture
{
    /// <summary>Guard configuration (source scan roots, allowlists).</summary>
    protected virtual ArchitectureGuardOptions Options => new();

    /// <summary>
    /// The repo's cross-schema edge manifest, validated against reality. Defaults to empty (the check
    /// passes vacuously); override to enable it.
    /// </summary>
    protected virtual IReadOnlyList<CrossSchemaEdge> Edges => [];

    [Test]
    public void Cross_schema_edge_manifest_is_valid()
    {
        var result = CrossSchemaGuards.EdgeManifestIsValid(Edges);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    [Test]
    public void Cross_schema_edge_resolvers_use_the_batched_loader()
    {
        var result = CrossSchemaGuards.EdgeResolversUseLoader(Options);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }
}
