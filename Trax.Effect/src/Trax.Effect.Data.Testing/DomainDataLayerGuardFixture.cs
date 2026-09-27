using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;
using Trax.Core.Testing;

// The [Test] method names are the documentation; XML doc comments on them would be pure redundancy.
#pragma warning disable CS1591

namespace Trax.Effect.Data.Testing;

/// <summary>
/// Pre-written data-layer guards. A consumer subclasses this, supplies <see cref="Options"/> (and, to
/// enable the schema-uniqueness check, <see cref="DomainContexts"/>), and runs <c>dotnet test</c>. No
/// test bodies to write.
/// </summary>
/// <remarks>
/// Example:
/// <code>
/// [TestFixture]
/// public sealed class MyDataGuards : DomainDataLayerGuardFixture
/// {
///     protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs", "apps"] };
///     protected override IReadOnlyList&lt;Type&gt; DomainContexts => [typeof(CatalogDbContext), typeof(LendingDbContext)];
///     protected override IReadOnlyList&lt;Type&gt; MigrationContexts => [typeof(CatalogDbContext)];
/// }
/// </code>
/// </remarks>
[TestFixture]
public abstract class DomainDataLayerGuardFixture
{
    /// <summary>Guard configuration (source scan roots, allowlists).</summary>
    protected abstract ArchitectureGuardOptions Options { get; }

    /// <summary>
    /// The repo's domain context types, for the schema-uniqueness check. Defaults to empty (the check
    /// passes vacuously); override to enable it.
    /// </summary>
    protected virtual IReadOnlyList<Type> DomainContexts => [];

    /// <summary>
    /// The repo's migration-based context types, for the snapshot-drift check. Defaults to empty (the
    /// check passes vacuously); override with the contexts that use migrations. Do not list contexts
    /// bootstrapped with <c>EnsureSchemaCreatedAsync</c>: they have no snapshot to compare against.
    /// </summary>
    protected virtual IReadOnlyList<Type> MigrationContexts => [];

    /// <summary>
    /// The models to run the owner-scope census over, one per context holding per-user data.
    /// Defaults to empty (the check passes vacuously); override together with
    /// <see cref="OwnerScope"/> to enable it. Build each from a context instance: an owner-scoped
    /// context usually takes its principal accessor through the constructor, and building the
    /// model needs no database connection.
    /// </summary>
    protected virtual IReadOnlyList<IReadOnlyModel> OwnerScopedModels => [];

    /// <summary>
    /// The owner type, principal accessor, and declarations for the owner-scope census. Required
    /// when <see cref="OwnerScopedModels"/> is not empty.
    /// </summary>
    protected virtual OwnerScopeCensusOptions? OwnerScope => null;

    /// <summary>The shared base type name the source guards look for. Defaults to <c>DomainDataContext</c>.</summary>
    protected virtual string BaseTypeName => "DomainDataContext";

    [Test]
    public void Domain_contexts_derive_the_shared_base()
    {
        var result = DataLayerGuards.DomainContextsDeriveBase(Options, BaseTypeName);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    [Test]
    public void Domain_contexts_have_companion_interfaces()
    {
        var result = DataLayerGuards.CompanionInterfaces(Options, BaseTypeName);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    [Test]
    public void Each_domain_context_owns_a_distinct_schema()
    {
        var result = DataLayerGuards.OneSchemaPerContext(DomainContexts);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    [Test]
    public void Each_migration_context_matches_its_snapshot()
    {
        var result = DataLayerGuards.NoPendingModelChanges(MigrationContexts);
        Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
    }

    [Test]
    public void Every_per_user_entity_is_scoped_to_its_owner()
    {
        var models = OwnerScopedModels;
        if (models.Count == 0)
            return;

        var options =
            OwnerScope
            ?? throw new InvalidOperationException(
                $"{GetType().Name} overrides OwnerScopedModels but not OwnerScope. The census "
                    + "needs the owner type and the principal accessor."
            );

        foreach (var model in models)
        {
            var result = DataLayerGuards.OwnerScopeCompleteness(model, options);
            Assert.That(result.Offenders, Is.Empty, result.FailureMessage);
        }
    }
}
