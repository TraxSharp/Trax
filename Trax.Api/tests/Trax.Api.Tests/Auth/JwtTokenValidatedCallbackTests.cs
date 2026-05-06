using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;

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
        using var sp = BuildProvider(new StubResolver());
        var opts = GetOptions(sp);
        var ctx = NewContext(sp, opts, principal: null, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("without a principal");
    }

    [Test]
    public async Task OnTokenValidated_NullSecurityToken_FailsContext()
    {
        using var sp = BuildProvider(new StubResolver());
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: null);

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("without a principal");
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

        using var sp = services.BuildServiceProvider();
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("ITraxPrincipalResolver<JwtTokenInput>");
    }

    [Test]
    public async Task OnTokenValidated_ResolverReturnsNull_FailsContext()
    {
        using var sp = BuildProvider(new StubResolver(returns: null));
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure!.Message.Should().Contain("known Trax principal");
    }

    [Test]
    public async Task OnTokenValidated_ResolverThrows_FailsWithException()
    {
        var boom = new InvalidOperationException("resolver explosion");
        using var sp = BuildProvider(new StubResolver(throws: boom));
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Failure.Should().BeSameAs(boom);
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
        using var sp = BuildProvider(new StubResolver(returns: trax));
        var opts = GetOptions(sp);
        var input = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u-1")], "test"));
        var ctx = NewContext(sp, opts, input, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().BeNull();
        ctx.Principal.Should().NotBeSameAs(input);
        ctx.Principal!.FindFirst(TraxAuthClaimTypes.PrincipalId)?.Value.Should().Be("u-1");
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

        using var sp = services.BuildServiceProvider();
        var opts = GetOptions(sp);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u")], "test"));
        var ctx = NewContext(sp, opts, principal, token: new JwtSecurityToken());

        await opts.Events.OnTokenValidated(ctx);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.Succeeded.Should().BeTrue();
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
            null,
            typeof(JwtBearerHandler)
        );
        var ctx = new TokenValidatedContext(http, scheme, opts)
        {
            Principal = principal,
            SecurityToken = token!,
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
