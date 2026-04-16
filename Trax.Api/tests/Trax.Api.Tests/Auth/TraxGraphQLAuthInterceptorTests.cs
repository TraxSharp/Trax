using FluentAssertions;
using HotChocolate;
using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class TraxGraphQLAuthInterceptorTests
{
    private static GraphQLConfiguration ConfigWithPolicy(string? policy) =>
        new(
            modelRegistrations: [],
            additionalTypeModules: [],
            schemaConfigurations: [],
            additionalTypeExtensions: [],
            authorizationRequired: true,
            authorizationPolicy: policy
        );

    private static (HttpContext context, OperationRequestBuilder builder) NewRequest()
    {
        var ctx = new DefaultHttpContext();
        var builder = OperationRequestBuilder.New().SetDocument("{ __typename }");
        return (ctx, builder);
    }

    [Test]
    public async Task OnCreateAsync_AuthorizationSucceeds_PassesThroughWithoutThrowing()
    {
        var authz = Substitute.For<IAuthorizationService>();
        authz
            .AuthorizeAsync(
                Arg.Any<System.Security.Claims.ClaimsPrincipal>(),
                Arg.Any<object?>(),
                Arg.Any<string>()
            )
            .Returns(AuthorizationResult.Success());

        var interceptor = new TraxGraphQLAuthInterceptor(authz, ConfigWithPolicy(policy: null));
        var (ctx, builder) = NewRequest();

        var act = async () =>
            await interceptor.OnCreateAsync(
                ctx,
                Substitute.For<IRequestExecutor>(),
                builder,
                CancellationToken.None
            );

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task OnCreateAsync_AuthorizationFails_ThrowsGraphQLException_WithTraxAuthorizationCode()
    {
        var authz = Substitute.For<IAuthorizationService>();
        authz
            .AuthorizeAsync(
                Arg.Any<System.Security.Claims.ClaimsPrincipal>(),
                Arg.Any<object?>(),
                Arg.Any<string>()
            )
            .Returns(AuthorizationResult.Failed());

        var interceptor = new TraxGraphQLAuthInterceptor(authz, ConfigWithPolicy(policy: null));
        var (ctx, builder) = NewRequest();

        var act = async () =>
            await interceptor.OnCreateAsync(
                ctx,
                Substitute.For<IRequestExecutor>(),
                builder,
                CancellationToken.None
            );

        var exception = (await act.Should().ThrowAsync<GraphQLException>()).Which;
        exception.Errors.Should().ContainSingle();
        exception.Errors[0].Code.Should().Be("TRAX_AUTHORIZATION");
        exception.Errors[0].Message.Should().Be("Not authorized.");
    }

    [Test]
    public async Task OnCreateAsync_NullPolicyOnConfig_FallsBackToTraxAuthPolicy()
    {
        var authz = Substitute.For<IAuthorizationService>();
        authz
            .AuthorizeAsync(
                Arg.Any<System.Security.Claims.ClaimsPrincipal>(),
                Arg.Any<object?>(),
                Arg.Any<string>()
            )
            .Returns(AuthorizationResult.Success());

        var interceptor = new TraxGraphQLAuthInterceptor(authz, ConfigWithPolicy(policy: null));
        var (ctx, builder) = NewRequest();

        await interceptor.OnCreateAsync(
            ctx,
            Substitute.For<IRequestExecutor>(),
            builder,
            CancellationToken.None
        );

        await authz
            .Received(1)
            .AuthorizeAsync(
                Arg.Any<System.Security.Claims.ClaimsPrincipal>(),
                Arg.Any<object?>(),
                Trax.Api.Auth.TraxAuthClaimTypes.TraxAuthPolicy
            );
    }

    [Test]
    public async Task OnCreateAsync_ExplicitPolicy_PassedToAuthorizationService()
    {
        var authz = Substitute.For<IAuthorizationService>();
        authz
            .AuthorizeAsync(
                Arg.Any<System.Security.Claims.ClaimsPrincipal>(),
                Arg.Any<object?>(),
                Arg.Any<string>()
            )
            .Returns(AuthorizationResult.Success());

        var interceptor = new TraxGraphQLAuthInterceptor(
            authz,
            ConfigWithPolicy(policy: "AdminOnly")
        );
        var (ctx, builder) = NewRequest();

        await interceptor.OnCreateAsync(
            ctx,
            Substitute.For<IRequestExecutor>(),
            builder,
            CancellationToken.None
        );

        await authz
            .Received(1)
            .AuthorizeAsync(
                Arg.Any<System.Security.Claims.ClaimsPrincipal>(),
                Arg.Any<object?>(),
                "AdminOnly"
            );
    }

    [Test]
    public void Interceptor_DerivesFromDefaultHttpRequestInterceptor_SoBcpAndIntrospectionPipelinesUntouched()
    {
        // Sanity check that fixes the contract: HotChocolate routes the BCP HTML
        // tool page and schema introspection through paths that do not invoke
        // IHttpRequestInterceptor.OnCreateAsync. By inheriting from the default
        // (which is an opt-in extension point), we automatically inherit that
        // behavior. If a future HC release changes that contract, this assertion
        // will not fail on its own but the docs and behavior need a fresh look.
        typeof(TraxGraphQLAuthInterceptor)
            .Should()
            .BeDerivedFrom<DefaultHttpRequestInterceptor>();
    }
}
