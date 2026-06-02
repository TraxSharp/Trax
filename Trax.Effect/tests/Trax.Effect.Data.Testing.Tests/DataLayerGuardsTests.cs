using Trax.Core.Testing;

namespace Trax.Effect.Data.Testing.Tests;

[TestFixture]
public class DataLayerGuardsTests
{
    private static ArchitectureGuardOptions OptionsFor(TempRepo repo) =>
        new() { RepoRootOverride = repo.Root, SourceScanRoots = ["src"] };

    private const string DerivedContext =
        "using Trax.Effect.Data.Services.DomainContext;\n"
        + "public class CatalogDbContext(DbContextOptions<CatalogDbContext> o)\n"
        + "    : DomainDataContext<CatalogDbContext>(o), ICatalogDbContext\n"
        + "{ protected override string Schema => \"catalog\"; }";

    private const string PlainContext =
        "public class CatalogDbContext(DbContextOptions<CatalogDbContext> o) : DbContext(o) { }";

    #region DomainContextsDeriveBase

    [Test]
    public void DomainContextsDeriveBase_PassesWhenDerived()
    {
        using var repo = new TempRepo().Write("src/Catalog/CatalogDbContext.cs", DerivedContext);

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Passed.Should().BeTrue(result.FailureMessage);
        result.Inspected.Should().Be(1);
    }

    [Test]
    public void DomainContextsDeriveBase_FlagsPlainDbContext()
    {
        using var repo = new TempRepo().Write("src/Catalog/CatalogDbContext.cs", PlainContext);

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("CatalogDbContext.cs"));
    }

    #endregion

    #region CompanionInterfaces

    [Test]
    public void CompanionInterfaces_PassesWhenSiblingExists()
    {
        using var repo = new TempRepo()
            .Write("src/Catalog/CatalogDbContext.cs", DerivedContext)
            .Write("src/Catalog/ICatalogDbContext.cs", "public interface ICatalogDbContext { }");

        DataLayerGuards.CompanionInterfaces(OptionsFor(repo)).Passed.Should().BeTrue();
    }

    [Test]
    public void CompanionInterfaces_FlagsMissingSibling()
    {
        using var repo = new TempRepo().Write("src/Catalog/CatalogDbContext.cs", DerivedContext);

        var result = DataLayerGuards.CompanionInterfaces(OptionsFor(repo));

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("ICatalogDbContext.cs"));
    }

    #endregion

    #region OneSchemaPerContext

    [Test]
    public void OneSchemaPerContext_PassesForDistinctSchemas()
    {
        var result = DataLayerGuards.OneSchemaPerContext([
            typeof(AlphaContext),
            typeof(BetaContext),
        ]);

        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void OneSchemaPerContext_FlagsSharedSchema()
    {
        var result = DataLayerGuards.OneSchemaPerContext([
            typeof(AlphaContext),
            typeof(DuplicateSchemaContext),
        ]);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains("alpha"));
    }

    #endregion

    #region NoPendingModelChanges

    [Test]
    public void NoPendingModelChanges_PassesVacuouslyForEmptyList()
    {
        var result = DataLayerGuards.NoPendingModelChanges([]);

        result.Passed.Should().BeTrue();
        result.Inspected.Should().Be(0);
    }

    [Test]
    public void NoPendingModelChanges_PassesWhenModelMatchesSnapshot()
    {
        // AlphaContext maps no entities, so its (empty) model has no tables the absent snapshot is
        // missing: no pending changes.
        var result = DataLayerGuards.NoPendingModelChanges([typeof(AlphaContext)]);

        result.Passed.Should().BeTrue(result.FailureMessage);
        result.Inspected.Should().Be(1);
    }

    [Test]
    public void NoPendingModelChanges_FlagsContextWithUnmigratedModel()
    {
        // PendingChangesContext maps a table but ships no migration capturing it.
        var result = DataLayerGuards.NoPendingModelChanges([typeof(PendingChangesContext)]);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().ContainSingle(o => o.Contains(nameof(PendingChangesContext)));
    }

    #endregion
}
