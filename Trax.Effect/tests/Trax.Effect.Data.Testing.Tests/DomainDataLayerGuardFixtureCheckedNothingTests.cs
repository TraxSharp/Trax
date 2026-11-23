using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework.Internal;
using Trax.Core.Testing;

namespace Trax.Effect.Data.Testing.Tests;

/// <summary>
/// The assertion the fixture's source guards make must fail when the guard inspected nothing: a
/// fixture pointed at the wrong directory otherwise passes every check.
/// </summary>
[TestFixture]
public sealed class DomainDataLayerGuardFixtureCheckedNothingTests
{
    [Test]
    public void Derive_base_guard_fails_when_it_finds_no_context()
    {
        using var repo = new TempRepo().Write("src/Catalog/Book.cs", "public class Book { }");

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo.Root, "src"));

        FailsInIsolation(() => DomainDataLayerGuardFixture.AssertCheckedAndClean(result));
    }

    [Test]
    public void Companion_interface_guard_fails_when_it_finds_no_context()
    {
        using var repo = new TempRepo().Write("src/Catalog/Book.cs", "public class Book { }");

        var result = DataLayerGuards.CompanionInterfaces(OptionsFor(repo.Root, "src"));

        FailsInIsolation(() => DomainDataLayerGuardFixture.AssertCheckedAndClean(result));
    }

    [Test]
    public void Derive_base_guard_fails_on_a_mistyped_scan_root()
    {
        using var repo = new TempRepo()
            .Write(
                "src/Catalog/CatalogDbContext.cs",
                "public class CatalogDbContext(DbContextOptions<CatalogDbContext> o)\n"
                    + "    : DomainDataContext<CatalogDbContext>(o), ICatalogDbContext { }"
            )
            .Write("src/Catalog/ICatalogDbContext.cs", "public interface ICatalogDbContext { }");

        var result = DataLayerGuards.DomainContextsDeriveBase(OptionsFor(repo.Root, "src", "scr"));

        FailsInIsolation(() => DomainDataLayerGuardFixture.AssertCheckedAndClean(result));
    }

    private static ArchitectureGuardOptions OptionsFor(string root, params string[] scanRoots) =>
        new() { RepoRootOverride = root, SourceScanRoots = scanRoots };

    // The assertion failure is recorded on an isolated result, so it can be observed without
    // failing this test.
    private static void FailsInIsolation(Action assertion)
    {
        using (new TestExecutionContext.IsolatedContext())
        {
            assertion.Should().Throw<AssertionException>();
        }
    }
}
