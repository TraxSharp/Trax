using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AwesomeAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Core.Extensions;

namespace Trax.Api.Tests.Auth;

/// <summary>
/// Drives the JWT OnTokenValidated event registered by AddTraxJwtAuth.
/// Tests cover the principal resolution, failure modes, and short-circuit
/// behaviour when an outer handler has already produced a Result.
/// </summary>
[TestFixture]
public class JwtTokenValidatedCallbackTests
{
    private static JwtBearerOptions GetOptions(IServiceProvider sp) =>
        sp.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtDefaults.SchemeName);

    [Test]
    public async Task OnTokenValidated_NullPrincipal_FailsContext()
    {
        var sp = BuildProvider(new StubResolver());
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var ctx = NewContext(sp, opts, principal: null, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result.NotNull().Failure.NotNull().Message.Should().Contain("without a principal");
    }

    [Test]
    public async Task OnTokenValidated_NullSecurityToken_FailsContext()
    {
        var sp = BuildProvider(new StubResolver());
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: null);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result.NotNull().Failure.NotNull().Message.Should().Contain("without a principal");
    }

    [Test]
    public async Task OnTokenValidated_NoResolverRegistered_FailsContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxJwtAuth("https://issuer.example.com", "aud");
        var resolver = services.Single(sd =>
            sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
        );
        services.Remove(resolver);

        var sp = services.BuildServiceProvider();
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result.NotNull()
            .Failure.NotNull()
            .Message.Should()
            .Contain("ITraxPrincipalResolver<JwtTokenInput>");
    }

    [Test]
    public async Task OnTokenValidated_ResolverReturnsNull_FailsContext()
    {
        var sp = BuildProvider(new StubResolver(returns: null));
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result.NotNull().Failure.NotNull().Message.Should().Contain("known Trax principal");
    }

    [Test]
    public async Task OnTokenValidated_ResolverThrows_FailsWithException()
    {
        var boom = new InvalidOperationException("resolver explosion");
        var sp = BuildProvider(new StubResolver(throws: boom));
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result.NotNull().Failure.Should().BeSameAs(boom);
    }

    [Test]
    public async Task OnTokenValidated_ResolverReturnsPrincipal_ReplacesPrincipal()
    {
        var trax = new TraxPrincipal(
            Id: "u-1",
            DisplayName: "User",
            Roles: ["admin"],
            Claims: null,
            PrincipalType: JwtDefaults.PrincipalType
        );
        var sp = BuildProvider(new StubResolver(returns: trax));
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var input = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u-1")], "test"));
        var ctx = NewContext(sp, opts, input, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal.Should().NotBeSameAs(input);
        ctx.Principal.NotNull()
            .FindFirst(TraxAuthClaimTypes.PrincipalId)
            ?.Value.Should()
            .Be("TraxJwt:u-1");
    }

    [Test]
    public async Task OnTokenValidated_ExistingHandlerSetsResult_ShortCircuits()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxJwtAuth(jwt =>
            jwt.UseAuthority("https://issuer.example.com", "aud")
                .CustomizeBearerOptions(o =>
                {
                    o.Events ??= new JwtBearerEvents();
                    o.Events.OnTokenValidated = c =>
                    {
                        c.Success();
                        return Task.CompletedTask;
                    };
                })
        );
        var resolver = services.Single(sd =>
            sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
        );
        services.Remove(resolver);
        services.AddSingleton<ITraxPrincipalResolver<JwtTokenInput>>(
            new StubResolver(throws: new Exception("must not run"))
        );

        var sp = services.BuildServiceProvider();
        await using var spDisposal = sp.ConfigureAwait(false);
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result.NotNull().Succeeded.Should().BeTrue();
    }

    private static ServiceProvider BuildProvider(StubResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddTraxJwtAuth("https://issuer.example.com", "aud");
        var registered = services.Single(sd =>
            sd.ServiceType == typeof(ITraxPrincipalResolver<JwtTokenInput>)
        );
        services.Remove(registered);
        services.AddSingleton<ITraxPrincipalResolver<JwtTokenInput>>(resolver);
        return services.BuildServiceProvider();
    }

    private static TokenValidatedContext NewContext(
        IServiceProvider sp,
        JwtBearerOptions opts,
        ClaimsPrincipal? principal,
        Microsoft.IdentityModel.Tokens.SecurityToken? token
    )
    {
        var http = new DefaultHttpContext { RequestServices = sp };
        var scheme = new AuthenticationScheme(
            JwtDefaults.SchemeName,
            displayName: null,
            typeof(JwtBearerHandler)
        );
        var ctx = new TokenValidatedContext(http, scheme, opts)
        {
            Principal = principal,
            SecurityToken = token ?? Null.Of<Microsoft.IdentityModel.Tokens.SecurityToken>(),
        };
        return ctx;
    }

    private sealed class StubResolver : ITraxPrincipalResolver<JwtTokenInput>
    {
        private readonly TraxPrincipal? _returns;
        private readonly Exception? _throws;

        public StubResolver(TraxPrincipal? returns = null, Exception? throws = null)
        {
            _returns = returns;
            _throws = throws;
        }

        public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct)
        {
            if (_throws is not null)
                throw _throws;
            return ValueTask.FromResult(_returns);
        }
    }
}
