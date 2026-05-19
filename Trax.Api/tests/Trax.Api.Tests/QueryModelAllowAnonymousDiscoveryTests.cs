using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Discovery shape for <see cref="TraxAllowAnonymousAttribute"/> on
/// <c>[TraxQueryModel]</c> entities. The attribute is discovered alongside
/// <see cref="TraxAuthorizeAttribute"/> in <see cref="TraxGraphQLBuilder.Build"/>
/// and surfaces as a boolean on <c>QueryModelRegistration.AllowAnonymous</c>.
/// The directive emission paths in the type module and the inverse invariant
/// in the schema validator both read this flag, so any drift in discovery
/// silently shifts both downstream surfaces.
///
/// <para>
/// The build-time mutual-exclusion guard with <c>[TraxAuthorize]</c> is also
/// exercised here: the two attributes are not allowed on the same entity,
/// whether declared directly or reached via inheritance.
/// </para>
/// </summary>
[TestFixture]
public class QueryModelAllowAnonymousDiscoveryTests
{
    // ── Discovery: AllowAnonymous flag is populated ──────────────────────

    [Test]
    public void Build_EntityWithAllowAnonymous_RegistrationAllowAnonymousIsTrue()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(AnonRow));
        reg.AllowAnonymous.Should().BeTrue();
        reg.AuthorizeAttributes.Should().BeEmpty();
    }

    [Test]
    public void Build_EntityWithoutAllowAnonymous_RegistrationAllowAnonymousIsFalse()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(BareRow));
        reg.AllowAnonymous.Should().BeFalse();
    }

    [Test]
    public void Build_EntityInheritingAllowAnonymousFromBase_DiscoversAsTrue()
    {
        // Inherited = true on the attribute means a base class declaring
        // [TraxAllowAnonymous] propagates to the derived entity. The Build
        // discovery pass must honor that — otherwise an anonymously-readable
        // base type silently locks down its subclasses.
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(InheritedAnonRow));
        reg.AllowAnonymous.Should().BeTrue();
    }

    [Test]
    public void Build_EntityImplementingAllowAnonymousInterface_DiscoversAsTrue()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r =>
            r.EntityType == typeof(ImplementsAnonInterfaceRow)
        );
        reg.AllowAnonymous.Should().BeTrue();
    }

    // ── Mutual exclusion with [TraxAuthorize] ────────────────────────────

    [Test]
    public void Build_BothAttributesOnSameEntity_Throws()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<ConflictContext>();

        var act = () => sut.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*[TraxAllowAnonymous]*[TraxAuthorize]*")
            .WithMessage($"*{typeof(ConflictedRow).FullName}*");
    }

    [Test]
    public void Build_AllowAnonymousFromInterfaceAuthorizeFromClass_Throws()
    {
        // The two attributes can reach the entity via different paths
        // (interface vs class). The mutual-exclusion guard must catch the
        // composition, not just the direct-declaration form, otherwise a
        // refactor that hoists [TraxAllowAnonymous] onto a shared interface
        // would silently un-gate every implementer that still carries
        // [TraxAuthorize].
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<MixedSourceContext>();

        var act = () => sut.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*[TraxAllowAnonymous]*[TraxAuthorize]*")
            .WithMessage($"*{typeof(MixedSourceRow).FullName}*");
    }

    [Test]
    public void Build_AllowAnonymousFromBaseAuthorizeFromDerived_Throws()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<MixedInheritanceContext>();

        var act = () => sut.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*[TraxAllowAnonymous]*[TraxAuthorize]*")
            .WithMessage($"*{typeof(MixedInheritanceRow).FullName}*");
    }

    // ── ExposeAs + AllowAnonymous compose, no conflict ───────────────────

    [Test]
    public void Build_ExposeAsAndAllowAnonymous_Coexist()
    {
        // ExposeAs is column-level (hides columns from the GraphQL surface).
        // AllowAnonymous is row-access (opens the entire entity to anonymous
        // reads). They are orthogonal; combining them is the natural way to
        // publish a public projection of a richer entity.
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<ExposeAsAnonContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single();
        reg.Attribute.ExposeAs.Should().Be(typeof(IExposedAnonRow));
        reg.AllowAnonymous.Should().BeTrue();
    }

    // ── Test entities ────────────────────────────────────────────────────

    [TraxQueryModel(Name = "bareRows")]
    [Table("bare_rows", Schema = "test_anon_disc")]
    public class BareRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "anonRows")]
    [TraxAllowAnonymous]
    [Table("anon_rows", Schema = "test_anon_disc")]
    public class AnonRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxAllowAnonymous]
    public abstract class AnonBase
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "inheritedAnonRows")]
    [Table("inherited_anon_rows", Schema = "test_anon_disc")]
    public class InheritedAnonRow : AnonBase { }

    [TraxAllowAnonymous]
    public interface IAnonInterface { }

    [TraxQueryModel(Name = "implementsAnonInterfaceRows")]
    [Table("implements_anon_iface_rows", Schema = "test_anon_disc")]
    public class ImplementsAnonInterfaceRow : IAnonInterface
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class DiscoveryContext(DbContextOptions<DiscoveryContext> options) : DbContext(options)
    {
        public DbSet<BareRow> BareRows { get; set; } = null!;
        public DbSet<AnonRow> AnonRows { get; set; } = null!;
        public DbSet<InheritedAnonRow> InheritedAnonRows { get; set; } = null!;
        public DbSet<ImplementsAnonInterfaceRow> ImplementsAnonInterfaceRows { get; set; } = null!;
    }

    [TraxQueryModel(Name = "conflictedRows")]
    [TraxAllowAnonymous]
    [TraxAuthorize(Roles = "Admin")]
    [Table("conflicted_rows", Schema = "test_anon_disc")]
    public class ConflictedRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class ConflictContext(DbContextOptions<ConflictContext> options) : DbContext(options)
    {
        public DbSet<ConflictedRow> Rows { get; set; } = null!;
    }

    [TraxAllowAnonymous]
    public interface IPubliclyVisible { }

    [TraxQueryModel(Name = "mixedSourceRows")]
    [TraxAuthorize(Roles = "Admin")]
    [Table("mixed_source_rows", Schema = "test_anon_disc")]
    public class MixedSourceRow : IPubliclyVisible
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class MixedSourceContext(DbContextOptions<MixedSourceContext> options)
        : DbContext(options)
    {
        public DbSet<MixedSourceRow> Rows { get; set; } = null!;
    }

    [TraxAllowAnonymous]
    public abstract class PubliclyVisibleBase
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "mixedInheritanceRows")]
    [TraxAuthorize(Roles = "Admin")]
    [Table("mixed_inheritance_rows", Schema = "test_anon_disc")]
    public class MixedInheritanceRow : PubliclyVisibleBase { }

    public class MixedInheritanceContext(DbContextOptions<MixedInheritanceContext> options)
        : DbContext(options)
    {
        public DbSet<MixedInheritanceRow> Rows { get; set; } = null!;
    }

    public interface IExposedAnonRow
    {
        long Id { get; }
        string Name { get; }
    }

    [TraxQueryModel(Name = "exposedAnonRows", ExposeAs = typeof(IExposedAnonRow))]
    [TraxAllowAnonymous]
    [Table("exposed_anon_rows", Schema = "test_anon_disc")]
    public class ExposedAnonRow : IExposedAnonRow
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("secret")]
        public string Secret { get; set; } = "";
    }

    public class ExposeAsAnonContext(DbContextOptions<ExposeAsAnonContext> options)
        : DbContext(options)
    {
        public DbSet<ExposedAnonRow> Rows { get; set; } = null!;
    }
}
