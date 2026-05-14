using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Startup;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Coverage for <see cref="QueryModelAuthorizationValidator"/>. Discovery shape
/// is exercised separately in <c>QueryModelAuthorizeDiscoveryTests</c>; runtime
/// enforcement is in <c>QueryModelAuthorizeE2ETests</c>. This file focuses on
/// the fail-loud startup check that every <c>[TraxAuthorize(Policy = ...)]</c>
/// references a policy the host has registered.
/// </summary>
[TestFixture]
public class QueryModelAuthorizeBuildTests
{
    [TraxQueryModel]
    [TraxAuthorize(Policy = "AdminPolicy")]
    private class PolicyGatedEntity
    {
        public int Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    private class RoleOnlyEntity
    {
        public int Id { get; set; }
    }

    private class PolicyDbContext(DbContextOptions<PolicyDbContext> options) : DbContext(options)
    {
        public DbSet<PolicyGatedEntity> Items { get; set; } = null!;
    }

    private class RolesOnlyDbContext(DbContextOptions<RolesOnlyDbContext> options)
        : DbContext(options)
    {
        public DbSet<RoleOnlyEntity> Items { get; set; } = null!;
    }

    [Test]
    public async Task Validator_PolicyRegistered_DoesNotThrow()
    {
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<PolicyDbContext>()
            .Build();

        var provider = Substitute.For<IAuthorizationPolicyProvider>();
        provider
            .GetPolicyAsync(Arg.Any<string>())
            .Returns(new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build());

        var validator = new QueryModelAuthorizationValidator(config, provider);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task Validator_PolicyMissing_ThrowsWithEntityAndPolicyName()
    {
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<PolicyDbContext>()
            .Build();

        var provider = Substitute.For<IAuthorizationPolicyProvider>();
        provider
            .GetPolicyAsync(Arg.Any<string>())
            .Returns(Task.FromResult<AuthorizationPolicy?>(null));

        var validator = new QueryModelAuthorizationValidator(config, provider);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*AdminPolicy*")
            .WithMessage("*PolicyGatedEntity*not registered*");
    }

    [Test]
    public async Task Validator_NoPolicies_DoesNotInvokeProvider()
    {
        // Entities gated only by roles (or bare [TraxAuthorize]) should never
        // hit the provider — the validator's job is policy reachability only.
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<RolesOnlyDbContext>()
            .Build();

        var provider = Substitute.For<IAuthorizationPolicyProvider>();
        var validator = new QueryModelAuthorizationValidator(config, provider);

        await validator.StartAsync(CancellationToken.None);

        await provider.DidNotReceive().GetPolicyAsync(Arg.Any<string>());
    }

    [TraxQueryModel]
    [TraxAuthorize(Policy = "AdminPolicy")]
    private class DupPolicyA
    {
        public int Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAuthorize(Policy = "AdminPolicy")]
    private class DupPolicyB
    {
        public int Id { get; set; }
    }

    private class DupPolicyDbContext(DbContextOptions<DupPolicyDbContext> options)
        : DbContext(options)
    {
        public DbSet<DupPolicyA> A { get; set; } = null!;
        public DbSet<DupPolicyB> B { get; set; } = null!;
    }

    [Test]
    public async Task Validator_SamePolicyOnMultipleEntities_QueriesProviderOnce()
    {
        // Two entities reference the same policy name. The validator's
        // dedup check (`seen.Add` returning false) must skip the second
        // lookup — proves we don't redundantly hit the policy provider once
        // per entity in production hosts where a single role policy is
        // applied to dozens of [TraxQueryModel] entities.
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<DupPolicyDbContext>()
            .Build();
        config.ModelRegistrations.Should().HaveCount(2);

        var provider = Substitute.For<IAuthorizationPolicyProvider>();
        provider
            .GetPolicyAsync("AdminPolicy")
            .Returns(new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build());

        var validator = new QueryModelAuthorizationValidator(config, provider);

        await validator.StartAsync(CancellationToken.None);

        await provider.Received(1).GetPolicyAsync("AdminPolicy");
    }
}
