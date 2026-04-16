using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TraxGraphQLAuthPolicyValidatorTests
{
    private static GraphQLConfiguration NewConfig(bool required, string? policy) =>
        new(
            modelRegistrations: [],
            additionalTypeModules: [],
            schemaConfigurations: [],
            additionalTypeExtensions: [],
            authorizationRequired: required,
            authorizationPolicy: policy
        );

    [Test]
    public async Task StartAsync_AuthorizationNotRequired_DoesNothing()
    {
        var services = new ServiceCollection().AddAuthorization();
        var sp = services.BuildServiceProvider();

        var validator = new TraxGraphQLAuthPolicyValidator(
            NewConfig(required: false, policy: null),
            sp.GetRequiredService<IAuthorizationPolicyProvider>()
        );

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_DefaultPolicy_RegisteredViaTraxAuth_Allows()
    {
        var services = new ServiceCollection();
        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                TraxAuthClaimTypes.TraxAuthPolicy,
                policy => policy.RequireAuthenticatedUser()
            );
        var sp = services.BuildServiceProvider();

        var validator = new TraxGraphQLAuthPolicyValidator(
            NewConfig(required: true, policy: null),
            sp.GetRequiredService<IAuthorizationPolicyProvider>()
        );

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_RequiredButDefaultPolicyMissing_ThrowsActionableMessage()
    {
        var services = new ServiceCollection().AddAuthorization();
        var sp = services.BuildServiceProvider();

        var validator = new TraxGraphQLAuthPolicyValidator(
            NewConfig(required: true, policy: null),
            sp.GetRequiredService<IAuthorizationPolicyProvider>()
        );

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*RequireAuthorization*TraxAuthPolicy*not registered*AddTraxApiKeyAuth*");
    }

    [Test]
    public async Task StartAsync_ExplicitPolicyRegistered_Allows()
    {
        var services = new ServiceCollection();
        services
            .AddAuthorizationBuilder()
            .AddPolicy("AdminOnly", policy => policy.RequireAuthenticatedUser());
        var sp = services.BuildServiceProvider();

        var validator = new TraxGraphQLAuthPolicyValidator(
            NewConfig(required: true, policy: "AdminOnly"),
            sp.GetRequiredService<IAuthorizationPolicyProvider>()
        );

        await validator.StartAsync(CancellationToken.None);
    }

    [Test]
    public async Task StartAsync_ExplicitPolicyMissing_ThrowsWithPolicyNameInMessage()
    {
        var services = new ServiceCollection().AddAuthorization();
        var sp = services.BuildServiceProvider();

        var validator = new TraxGraphQLAuthPolicyValidator(
            NewConfig(required: true, policy: "AdminOnly"),
            sp.GetRequiredService<IAuthorizationPolicyProvider>()
        );

        var act = async () => await validator.StartAsync(CancellationToken.None);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*RequireAuthorization(\"AdminOnly\")*'AdminOnly'*not registered*");
    }
}
