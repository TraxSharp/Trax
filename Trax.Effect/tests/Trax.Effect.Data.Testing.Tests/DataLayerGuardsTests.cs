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

    [Test]
    public void DomainContextsDeriveBase_FlagsAPlainDbContextBesideAMentionOfTheBase()
    {
        // The base named elsewhere in the file (a sibling class, a using alias) is not this
        // class deriving it.
        using var repo = new TempRepo().Write(
            "src/Catalog/CatalogDbContext.cs",
            "public class CatalogDbContext(DbContextOptions<CatalogDbContext> o) : DbContext(o) { }\n"
                + "public abstract class CatalogBase : DomainDataContext<CatalogDbContext> { }"
        );

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Offenders.Should().ContainSingle(o => o.Contains("CatalogDbContext"));
    }

    [Test]
    public void DomainContextsDeriveBase_JudgesEachContextInAFile()
    {
        using var repo = new TempRepo().Write(
            "src/Catalog/Contexts.cs",
            DerivedContext
                + "\npublic class AuditDbContext(DbContextOptions<AuditDbContext> o) : DbContext(o) { }"
        );

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Inspected.Should().Be(2);
        result.Offenders.Should().ContainSingle(o => o.Contains("AuditDbContext"));
    }

    [Test]
    public void DomainContextsDeriveBase_IgnoresAContextNamedOnlyInACommentOrString()
    {
        using var repo = new TempRepo().Write(
            "src/Catalog/Notes.cs",
            "// class LegacyDbContext : DbContext\n"
                + "public class Notes { string s = \"class OldDbContext : DbContext\"; }"
        );

        DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo)).Inspected.Should().Be(0);
    }

    [Test]
    public void DomainContextsDeriveBase_AcceptsTheBaseNamedByAQualifiedOrGlobalName()
    {
        using var repo = new TempRepo()
            .Write(
                "src/Catalog/CatalogDbContext.cs",
                "public class CatalogDbContext(DbContextOptions<CatalogDbContext> o)\n"
                    + "    : Trax.Effect.Data.Services.DomainContext.DomainDataContext<CatalogDbContext>(o) { }"
            )
            .Write(
                "src/Audit/AuditDbContext.cs",
                "public class AuditDbContext(DbContextOptions<AuditDbContext> o)\n"
                    + "    : global::DomainDataContext<AuditDbContext>(o) { }"
            );

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Inspected.Should().Be(2);
        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void DomainContextsDeriveBase_MergesThePartsOfAPartialContextInANamespace()
    {
        // The base list is on one part only; the other part, in its own file, does not repeat it.
        using var repo = new TempRepo()
            .Write(
                "src/Catalog/CatalogDbContext.cs",
                "namespace Shop.Catalog;\n"
                    + "public partial class CatalogDbContext(DbContextOptions<CatalogDbContext> o)\n"
                    + "    : DomainDataContext<CatalogDbContext>(o) { }"
            )
            .Write(
                "src/Catalog/CatalogDbContext.Sets.cs",
                "namespace Shop.Catalog;\n"
                    + "public partial class CatalogDbContext { public DbSet<Item> Items => Set<Item>(); }"
            );

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Inspected.Should().Be(1, "the two parts are one context");
        result.Passed.Should().BeTrue(result.FailureMessage);
    }

    [Test]
    public void DomainContextsDeriveBase_FlagsAContextWhoseBaseIsAPredefinedType()
    {
        using var repo = new TempRepo().Write(
            "src/Catalog/CatalogDbContext.cs",
            "public class CatalogDbContext : object { }"
        );

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo));

        result.Offenders.Should().ContainSingle(o => o.Contains("CatalogDbContext"));
    }

    [Test]
    public void DomainContextsDeriveBase_FlagsAMissingScanRoot()
    {
        using var repo = new TempRepo().Write("src/Catalog/CatalogDbContext.cs", DerivedContext);

        var result = DataLayerGuards.DomainContextsDeriveBase(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["src", "scr"],
            }
        );

        result.Passed.Should().BeFalse("a mistyped root would otherwise scan nothing and pass");
        result.Offenders.Should().ContainSingle(o => o.Contains("scr"));
    }

    #endregion

    #region CompanionInterfaces

    [Test]
    public void CompanionInterfaces_FlagsAnOrdinaryConstructorContextWithoutItsInterface()
    {
        using var repo = new TempRepo().Write(
            "src/Catalog/CatalogDbContext.cs",
            "public class CatalogDbContext : DomainDataContext<CatalogDbContext>\n"
                + "{\n"
                + "    public CatalogDbContext(DbContextOptions<CatalogDbContext> o) : base(o) { }\n"
                + "    protected override string Schema => \"catalog\";\n"
                + "}"
        );

        var result = DataLayerGuards.CompanionInterfaces(OptionsFor(repo));

        result.Inspected.Should().Be(1);
        result.Offenders.Should().ContainSingle(o => o.Contains("ICatalogDbContext.cs"));
    }

    [Test]
    public void CompanionInterfaces_FlagsAMissingScanRoot()
    {
        using var repo = new TempRepo()
            .Write("src/Catalog/CatalogDbContext.cs", DerivedContext)
            .Write("src/Catalog/ICatalogDbContext.cs", "public interface ICatalogDbContext { }");

        var result = DataLayerGuards.CompanionInterfaces(
            new ArchitectureGuardOptions
            {
                RepoRootOverride = repo.Root,
                SourceScanRoots = ["libs"],
            }
        );

        result.Offenders.Should().ContainSingle(o => o.Contains("libs"));
    }

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
