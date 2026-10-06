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
    /// Whether the scan roots are expected to hold at least one cross-schema
    /// <c>[ExtendObjectType]</c> resolver. Defaults to <c>true</c>, so a scan that finds none,
    /// usually because <see cref="ArchitectureGuardOptions.SourceScanRoots"/> point somewhere the
    /// code is not, fails instead of passing on nothing. Override to <c>false</c> in a repo that
    /// has no cross-schema edges.
    /// </summary>
    protected virtual bool ExpectsCrossSchemaResolvers => true;

    /// <summary>
    /// Whether the scan roots are expected to hold at least one <c>[Parent]</c> resolver on an
    /// <c>[ExtendObjectType]</c> class. Defaults to <c>true</c> for the same reason as
    /// <see cref="ExpectsCrossSchemaResolvers"/>; override to <c>false</c> in a repo that has none.
    /// </summary>
    protected virtual bool ExpectsParentResolvers => true;

    /// <summary>
    /// Fails when a cross-schema <c>[ExtendObjectType]</c> resolver does not go through a
    /// <c>CrossSchemaLoader</c>, or when the scan found none to check. See
    /// <see cref="CrossSchemaGuards.EdgeResolversUseLoader"/>.
    /// </summary>
    [Test]
    public void Cross_schema_edge_resolvers_use_the_batched_loader()
    {
        var options = Options;
        var result = CrossSchemaGuards.EdgeResolversUseLoader(options);
        AssertInspected(
            result,
            ExpectsCrossSchemaResolvers,
            "cross-schema [ExtendObjectType] resolver",
            nameof(ExpectsCrossSchemaResolvers),
            options
        );
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    /// <summary>
    /// Fails when a <c>[Parent]</c> resolver reads a property other than the key without declaring
    /// it in <c>[Parent(requires: ...)]</c>, or when the scan found none to check. See
    /// <see cref="CrossSchemaGuards.ExtensionResolversDeclareParentRequirements"/>.
    /// </summary>
    [Test]
    public void Extension_resolvers_declare_what_they_read_off_their_parent()
    {
        var options = Options;
        var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(options);
        AssertInspected(
            result,
            ExpectsParentResolvers,
            "[Parent] resolver",
            nameof(ExpectsParentResolvers),
            options
        );
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    /// <summary>
    /// A source guard that inspected nothing proves nothing: with scan roots that miss the code,
    /// it would pass every check. Fails on that first, naming the roots it scanned.
    /// </summary>
    protected static void AssertInspected(
        GuardResult result,
        bool expected,
        string what,
        string optOut,
        ArchitectureGuardOptions options
    )
    {
        if (!expected || result.Inspected > 0)
            return;

        Assert.Fail(
            $"The scan found no {what} under the source scan roots "
                + $"[{string.Join(", ", options.SourceScanRoots)}] of "
                + $"'{options.RepoRootOverride ?? "the repository root"}', so the guard checked "
                + "nothing. Point ArchitectureGuardOptions.SourceScanRoots at the code, or, if "
                + $"the repo really has none, override {optOut} to return false."
        );
    }
}
