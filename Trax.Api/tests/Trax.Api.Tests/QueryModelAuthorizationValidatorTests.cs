using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Startup;
using Trax.Effect.Attributes;

namespace Trax.Api.Tests;

/// <summary>
/// Direct unit coverage for <see cref="QueryModelAuthorizationValidator"/>.
/// The validator's whole reason for existing is to refuse to start a host
/// whose gated entities point at policies the consumer never registered —
/// the exact failure that, if missed, would turn into a silent deny-all in
/// production. These tests pin both directions of that check.
/// </summary>
[TestFixture]
public class QueryModelAuthorizationValidatorTests
{
    [TraxQueryModel]
    [TraxAuthorize(Policy = "RegisteredPolicy")]
    private class GatedWithRegisteredPolicy
    {
        public int Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAuthorize(Policy = "MissingPolicy")]
    private class GatedWithMissingPolicy
    {
        public int Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAuthorize(Roles = "Admin")]
    private class GatedRolesOnly
    {
        public int Id { get; set; }
    }

    private class RegisteredPolicyDbContext(DbContextOptions<RegisteredPolicyDbContext> options)
        : DbContext(options)
    {
        public DbSet<GatedWithRegisteredPolicy> Rows { get; set; } = null!;
    }

    private class MissingPolicyDbContext(DbContextOptions<MissingPolicyDbContext> options)
        : DbContext(options)
    {
        public DbSet<GatedWithMissingPolicy> Rows { get; set; } = null!;
    }

    private class RolesOnlyDbContext(DbContextOptions<RolesOnlyDbContext> options)
        : DbContext(options)
    {
        public DbSet<GatedRolesOnly> Rows { get; set; } = null!;
    }

    [Test]
    public async Task Validator_RegisteredPolicy_DoesNotThrow()
    {
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<RegisteredPolicyDbContext>()
            .Build();

        var services = new ServiceCollection();
        services.AddAuthorization(opts =>
            opts.AddPolicy("RegisteredPolicy", p => p.RequireAuthenticatedUser())
        );
        var policyProvider = services
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationPolicyProvider>();

        var validator = new QueryModelAuthorizationValidator(config, policyProvider);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
    }

    [Test]
    public async Task Validator_MissingPolicy_ThrowsNamingPolicyAndEntity()
    {
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<MissingPolicyDbContext>()
            .Build();

        var services = new ServiceCollection();
        services.AddAuthorization(); // Default options only; "MissingPolicy" is intentionally not registered.
        var policyProvider = services
            .BuildServiceProvider()
            .GetRequiredService<IAuthorizationPolicyProvider>();

        var validator = new QueryModelAuthorizationValidator(config, policyProvider);

        var act = async () => await validator.StartAsync(CancellationToken.None);

        var assertion = await act.Should().ThrowAsync<InvalidOperationException>();
        assertion
            .Which.Message.Should()
            .Contain("MissingPolicy", "the error must name the policy the consumer typo'd")
            .And.Contain(
                typeof(GatedWithMissingPolicy).FullName!,
                "the error must name the entity that declared the policy"
            )
            .And.Contain("AddAuthorization", "the error must point the consumer at the fix");
    }

    [Test]
    public async Task Validator_RolesOnlyEntity_NeverConsultsPolicyProvider()
    {
        // Roles-only attributes do not reference an ASP.NET Core policy, so the
        // validator must skip them entirely. Pin that with a probe provider
        // that throws on any GetPolicyAsync call.
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<RolesOnlyDbContext>()
            .Build();
        config
            .ModelRegistrations.Single()
            .AuthorizeAttributes.Should()
            .HaveCount(1, "the fixture must register one [TraxAuthorize(Roles=...)] attribute");

        var policyProvider = new ThrowingPolicyProvider();

        var validator = new QueryModelAuthorizationValidator(config, policyProvider);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync(
                "a roles-only attribute carries no policy name, so the validator must not consult the policy provider"
            );
        policyProvider.GetPolicyCallCount.Should().Be(0);
    }

    [Test]
    public async Task Validator_DuplicatePolicyReferences_ConsultsProviderOncePerName()
    {
        // Two entities reference the same policy. The validator deduplicates
        // by policy name before calling GetPolicyAsync — pin the optimisation
        // so a refactor that drops the `seen` set does not silently blow up
        // policy-provider call volume on large schemas.
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<DuplicatePolicyDbContext>()
            .Build();
        config
            .ModelRegistrations.Should()
            .HaveCount(
                2,
                "the fixture must register two entities for the dedup check to be meaningful"
            );

        var policyProvider = new CountingPolicyProvider();
        // Make the policy resolvable so the test isolates the call-count check
        // from the missing-policy throw path.
        policyProvider.Register(
            "SharedPolicy",
            new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
        );

        var validator = new QueryModelAuthorizationValidator(config, policyProvider);

        await validator
            .Invoking(v => v.StartAsync(CancellationToken.None))
            .Should()
            .NotThrowAsync();
        policyProvider
            .GetPolicyCallCount.Should()
            .Be(1, "the validator must dedup policy lookups by name across entities");
    }

    [Test]
    public async Task Validator_StopAsync_CompletesSynchronously()
    {
        // The validator owns no resources to release. Pinning that StopAsync
        // returns a synchronously-completed Task forces a future refactor that
        // introduces async cleanup to update this contract explicitly.
        var config = new TraxGraphQLBuilder(new ServiceCollection())
            .AddDbContext<RolesOnlyDbContext>()
            .Build();
        var validator = new QueryModelAuthorizationValidator(config, new ThrowingPolicyProvider());

        var task = validator.StopAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
        await task;
    }

    [TraxQueryModel]
    [TraxAuthorize(Policy = "SharedPolicy")]
    private class FirstGatedBySharedPolicy
    {
        public int Id { get; set; }
    }

    [TraxQueryModel]
    [TraxAuthorize(Policy = "SharedPolicy")]
    private class SecondGatedBySharedPolicy
    {
        public int Id { get; set; }
    }

    private class DuplicatePolicyDbContext(DbContextOptions<DuplicatePolicyDbContext> options)
        : DbContext(options)
    {
        public DbSet<FirstGatedBySharedPolicy> First { get; set; } = null!;
        public DbSet<SecondGatedBySharedPolicy> Second { get; set; } = null!;
    }

    /// <summary>
    /// Throws on any call to <see cref="GetPolicyAsync"/> so a test can prove
    /// the validator's roles-only short-circuit never reaches the provider.
    /// </summary>
    private sealed class ThrowingPolicyProvider : IAuthorizationPolicyProvider
    {
        public int GetPolicyCallCount { get; private set; }

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            throw new InvalidOperationException("validator must not request the default policy");

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            throw new InvalidOperationException("validator must not request the fallback policy");

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            GetPolicyCallCount++;
            throw new InvalidOperationException(
                $"validator must not look up policy '{policyName}' for a roles-only attribute"
            );
        }
    }

    /// <summary>
    /// Records every policy-name lookup so the dedup test can assert the
    /// validator called the provider exactly once per distinct name.
    /// </summary>
    private sealed class CountingPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly Dictionary<string, AuthorizationPolicy> _policies = new(
            StringComparer.Ordinal
        );

        public int GetPolicyCallCount { get; private set; }

        public void Register(string name, AuthorizationPolicy policy) => _policies[name] = policy;

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
            Task.FromResult(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            Task.FromResult<AuthorizationPolicy?>(null);

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            GetPolicyCallCount++;
            _policies.TryGetValue(policyName, out var policy);
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
    }
}
