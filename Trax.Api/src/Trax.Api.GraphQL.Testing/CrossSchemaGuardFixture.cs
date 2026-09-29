using NUnit.Framework;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Core.Testing;

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

    /// <summary>
    /// Fails when an edge in <see cref="Edges"/> names a foreign key, target or field that does
    /// not exist as declared. See <see cref="CrossSchemaGuards.EdgeManifestIsValid"/>.
    /// </summary>
    [Test]
    public void Cross_schema_edge_manifest_is_valid()
    {
        var result = CrossSchemaGuards.EdgeManifestIsValid(Edges);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    /// <summary>
    /// Fails when a cross-schema <c>[ExtendObjectType]</c> resolver does not go through a
    /// <c>CrossSchemaLoader</c>. See <see cref="CrossSchemaGuards.EdgeResolversUseLoader"/>.
    /// </summary>
    [Test]
    public void Cross_schema_edge_resolvers_use_the_batched_loader()
    {
        var result = CrossSchemaGuards.EdgeResolversUseLoader(Options);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    /// <summary>
    /// Fails when a <c>[Parent]</c> resolver reads a property other than the key without declaring
    /// it in <c>[Parent(requires: ...)]</c>. See
    /// <see cref="CrossSchemaGuards.ExtensionResolversDeclareParentRequirements"/>.
    /// </summary>
    [Test]
    public void Extension_resolvers_declare_what_they_read_off_their_parent()
    {
        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(Options);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }
}
