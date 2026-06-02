using System.ComponentModel.DataAnnotations.Schema;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Exposure authorization for <c>[TraxQueryModel]</c> entities, enforced in
/// <see cref="TraxGraphQLBuilder.Build"/>. An exposed entity must declare its posture
/// explicitly: on an open endpoint it needs <c>[TraxAuthorize]</c> or
/// <c>[TraxAllowAnonymous]</c>; <c>[TraxAllowAnonymous]</c> is contradictory once the
/// endpoint is gated via <c>RequireAuthorization()</c>. Mirrors the train-side suite.
/// </summary>
[TestFixture]
public class QueryModelExposureAuthorizationTests
{
    [Test]
    public void OpenEndpoint_BareEntity_Throws()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<BareContext>();

        var act = () => sut.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*neither [TraxAuthorize] nor [TraxAllowAnonymous]*")
            .WithMessage($"*{typeof(BareRow).FullName}*");
    }

    [Test]
    public void OpenEndpoint_AuthorizeEntity_Succeeds()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<AuthorizedContext>();

        var config = sut.Build();

        config
            .ModelRegistrations.Should()
            .ContainSingle(r => r.EntityType == typeof(AuthorizedRow));
    }

    [Test]
    public void OpenEndpoint_AllowAnonymousEntity_Succeeds()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.AddDbContext<AnonContext>();

        var config = sut.Build();

        config.ModelRegistrations.Should().ContainSingle(r => r.EntityType == typeof(AnonRow));
    }

    [Test]
    public void GatedEndpoint_BareEntity_Succeeds()
    {
        // The endpoint gate covers the entity, so a missing marker is fine.
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.RequireAuthorization();
        sut.AddDbContext<BareContext>();

        var config = sut.Build();

        config.ModelRegistrations.Should().ContainSingle(r => r.EntityType == typeof(BareRow));
    }

    [Test]
    public void GatedEndpoint_AuthorizeEntity_Succeeds()
    {
        // Endpoint gate plus per-entity [TraxAuthorize] compose; not a conflict.
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.RequireAuthorization();
        sut.AddDbContext<AuthorizedContext>();

        var config = sut.Build();

        config
            .ModelRegistrations.Should()
            .ContainSingle(r => r.EntityType == typeof(AuthorizedRow));
    }

    [Test]
    public void GatedEndpoint_AllowAnonymousEntity_Throws()
    {
        var sut = new TraxGraphQLBuilder(new ServiceCollection());
        sut.RequireAuthorization();
        sut.AddDbContext<AnonContext>();

        var act = () => sut.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*RequireAuthorization*")
            .WithMessage($"*{typeof(AnonRow).FullName}*");
    }

    // ── Test entities ────────────────────────────────────────────────────

    [TraxQueryModel(Name = "exposureBareRows")]
    [Table("bare_rows", Schema = "test_exposure")]
    public class BareRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "exposureAuthorizedRows")]
    [TraxAuthorize(Roles = "Admin")]
    [Table("authorized_rows", Schema = "test_exposure")]
    public class AuthorizedRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    [TraxQueryModel(Name = "exposureAnonRows")]
    [TraxAllowAnonymous]
    [Table("anon_rows", Schema = "test_exposure")]
    public class AnonRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    public class BareContext(DbContextOptions<BareContext> options) : DbContext(options)
    {
        public DbSet<BareRow> Rows { get; set; } = null!;
    }

    public class AuthorizedContext(DbContextOptions<AuthorizedContext> options) : DbContext(options)
    {
        public DbSet<AuthorizedRow> Rows { get; set; } = null!;
    }

    public class AnonContext(DbContextOptions<AnonContext> options) : DbContext(options)
    {
        public DbSet<AnonRow> Rows { get; set; } = null!;
    }
}
