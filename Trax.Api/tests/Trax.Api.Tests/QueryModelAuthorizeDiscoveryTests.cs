using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Unit coverage for how <see cref="TraxGraphQLBuilder.Build"/> discovers
/// <c>[TraxAuthorize]</c> on <c>[TraxQueryModel]</c> entities and threads the
/// attribute set through <c>QueryModelRegistration.AuthorizeAttributes</c>.
/// The E2E suite proves the directive is wired correctly through the full
/// HotChocolate pipeline; these tests pin the discovery surface so the data
/// reaching the type module is the source of truth a future refactor must
/// preserve.
/// </summary>
[TestFixture]
public class QueryModelAuthorizeDiscoveryTests
{
    // ── Discovery: AuthorizeAttributes is populated ──────────────────────

    [Test]
    public void Build_EntityWithSingleRoleAttribute_ExposesIt()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(GatedRow));
        reg.AuthorizeAttributes.Should().HaveCount(1);
        reg.AuthorizeAttributes[0].Roles.Should().Be("Admin");
    }

    [Test]
    public void Build_EntityWithoutAuthorize_HasEmptyList()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(OpenRow));
        reg.AuthorizeAttributes.Should().BeEmpty();
    }

    [Test]
    public void Build_EntityWithStackedAuthorize_DiscoversAllAttributes()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(StackedRow));
        reg.AuthorizeAttributes.Should().HaveCount(2);
        reg.AuthorizeAttributes.Should().Contain(a => a.Roles == "Admin");
        reg.AuthorizeAttributes.Should().Contain(a => a.Policy == "AdminPolicy");
    }

    [Test]
    public void Build_EntityInheritingAuthorize_DiscoversFromBase()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(InheritedGatedRow));
        reg.AuthorizeAttributes.Should().HaveCount(1);
        reg.AuthorizeAttributes[0].Roles.Should().Be("BaseAdmin");
    }

    [Test]
    public void Build_EntityWithBareAuthorize_RecordsAttributeWithoutPolicyOrRoles()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<DiscoveryContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single(r => r.EntityType == typeof(BareGatedRow));
        reg.AuthorizeAttributes.Should().HaveCount(1);
        reg.AuthorizeAttributes[0].Policy.Should().BeNull();
        reg.AuthorizeAttributes[0].Roles.Should().BeNull();
    }

    // ── Build-time shape validation ──────────────────────────────────────

    [Test]
    public void Build_WhitespacePolicy_Throws()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<WhitespacePolicyContext>();

        var act = () => sut.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Policy value*")
            .WithMessage($"*{typeof(WhitespacePolicyRow).FullName}*");
    }

    [Test]
    public void Build_RolesEmptyAfterSplit_Throws()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<EmptyRolesContext>();

        var act = () => sut.Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*parsed to zero roles*");
    }

    // ── ExposeAs + [TraxAuthorize] compose, no conflict ──────────────────

    [Test]
    public void Build_ExposeAsAndAuthorize_Coexist()
    {
        var sut = new TraxGraphQLBuilder(
            new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        );
        sut.AddDbContext<ExposeAsAuthorizeContext>();

        var config = sut.Build();

        var reg = config.ModelRegistrations.Single();
        reg.Attribute.ExposeAs.Should().Be(typeof(IExposedRow));
        reg.AuthorizeAttributes.Should().HaveCount(1);
        reg.AuthorizeAttributes[0].Roles.Should().Be("Admin");
    }

    // ── Test entities ────────────────────────────────────────────────────

    [TraxQueryModel(Name = "openRows")]
    [Table("open_rows", Schema = "test_disc")]
    public class OpenRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "gatedRows")]
    [TraxAuthorize(Roles = "Admin")]
    [Table("gated_rows", Schema = "test_disc")]
    public class GatedRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "stackedRows")]
    [TraxAuthorize(Roles = "Admin")]
    [TraxAuthorize(Policy = "AdminPolicy")]
    [Table("stacked_rows", Schema = "test_disc")]
    public class StackedRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxAuthorize(Roles = "BaseAdmin")]
    public abstract class GatedBase
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "inheritedGatedRows")]
    [Table("inherited_gated_rows", Schema = "test_disc")]
    public class InheritedGatedRow : GatedBase { }

    [TraxQueryModel(Name = "bareGatedRows")]
    [TraxAuthorize]
    [Table("bare_gated_rows", Schema = "test_disc")]
    public class BareGatedRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class DiscoveryContext(DbContextOptions<DiscoveryContext> options) : DbContext(options)
    {
        public DbSet<OpenRow> OpenRows { get; set; } = null!;
        public DbSet<GatedRow> GatedRows { get; set; } = null!;
        public DbSet<StackedRow> StackedRows { get; set; } = null!;
        public DbSet<InheritedGatedRow> InheritedGatedRows { get; set; } = null!;
        public DbSet<BareGatedRow> BareGatedRows { get; set; } = null!;
    }

    [TraxQueryModel(Name = "whitespacePolicy")]
    [TraxAuthorize(Policy = "   ")]
    [Table("ws_policy_rows", Schema = "test_disc")]
    public class WhitespacePolicyRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class WhitespacePolicyContext(DbContextOptions<WhitespacePolicyContext> options)
        : DbContext(options)
    {
        public DbSet<WhitespacePolicyRow> Rows { get; set; } = null!;
    }

    [TraxQueryModel(Name = "emptyRolesRows")]
    [TraxAuthorize(Roles = ",,,")]
    [Table("empty_roles_rows", Schema = "test_disc")]
    public class EmptyRolesRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class EmptyRolesContext(DbContextOptions<EmptyRolesContext> options) : DbContext(options)
    {
        public DbSet<EmptyRolesRow> Rows { get; set; } = null!;
    }

    public interface IExposedRow
    {
        long Id { get; }
        string Name { get; }
    }

    [TraxQueryModel(Name = "exposedRows", ExposeAs = typeof(IExposedRow))]
    [TraxAuthorize(Roles = "Admin")]
    [Table("exposed_rows", Schema = "test_disc")]
    public class ExposedRow : IExposedRow
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("name")]
        public string Name { get; set; } = "";

        [Column("secret")]
        public string Secret { get; set; } = "";
    }

    public class ExposeAsAuthorizeContext(DbContextOptions<ExposeAsAuthorizeContext> options)
        : DbContext(options)
    {
        public DbSet<ExposedRow> Rows { get; set; } = null!;
    }
}
