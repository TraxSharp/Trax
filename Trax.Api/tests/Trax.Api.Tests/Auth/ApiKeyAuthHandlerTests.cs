using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class ApiKeyAuthHandlerTests
{
    private static async Task<IHost> CreateHost(
        Func<string, CancellationToken, ValueTask<TraxPrincipal?>> resolver,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null,
        TestLoggerProvider? loggerProvider = null
    )
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddTraxApiKeyAuthWithInstance(
                            new DelegateResolver(resolver),
                            configureOptions
                        );
                        if (loggerProvider is not null)
                        {
                            services.AddLogging(lb =>
                            {
                                lb.ClearProviders();
                                lb.SetMinimumLevel(LogLevel.Trace);
                                lb.AddProvider(loggerProvider);
                            });
                        }
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints
                                .MapGet(
                                    "/protected",
                                    (ClaimsPrincipal user) =>
                                        Results.Ok(
                                            new ProtectedResponse
                                            {
                                                Name = user.Identity?.Name,
                                                PrincipalId = user.FindFirst(
                                                    TraxAuthClaimTypes.PrincipalId
                                                )?.Value,
                                                Roles = user.FindAll(ClaimTypes.Role)
                                                    .Select(c => c.Value)
                                                    .ToArray(),
                                                TenantClaim = user.FindFirst("tenant")?.Value,
                                            }
                                        )
                                )
                                .RequireAuthorization(ApiKeyDefaults.PolicyName);

                            endpoints.MapGet("/anonymous", () => Results.Ok("ok")).AllowAnonymous();

                            endpoints.MapGet(
                                "/authresult",
                                async (HttpContext ctx) =>
                                {
                                    var result = await ctx.AuthenticateAsync(
                                        ApiKeyDefaults.SchemeName
                                    );
                                    return Results.Ok(
                                        new AuthResultResponse
                                        {
                                            Succeeded = result.Succeeded,
                                            None = result.None,
                                            FailureMessage = result.Failure?.Message,
                                        }
                                    );
                                }
                            );
                        });
                    })
            )
            .Build();

        await host.StartAsync();
        return host;
    }

    #region Header handling

    [Test]
    public async Task MissingHeader_AuthenticateReturnsNoResult_NotFail()
    {
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();

        var response = await client.GetFromJsonAsync<AuthResultResponse>("/authresult");

        response.Should().NotBeNull();
        response!.None.Should().BeTrue();
        response.Succeeded.Should().BeFalse();
        response.FailureMessage.Should().BeNull();
    }

    [Test]
    public async Task MissingHeader_AllowAnonymous_Endpoint200()
    {
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();

        var response = await client.GetAsync("/anonymous");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task MissingHeader_ProtectedEndpoint_Returns401()
    {
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task InvalidHeader_AuthenticateReturnsFail()
    {
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "bad-key");

        var response = await client.GetFromJsonAsync<AuthResultResponse>("/authresult");

        response.Should().NotBeNull();
        response!.Succeeded.Should().BeFalse();
        response.None.Should().BeFalse();
        response.FailureMessage.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task InvalidHeader_ProtectedEndpoint_Returns401()
    {
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "bad-key");

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CustomHeaderName_Respected()
    {
        using var host = await CreateHost(
            (key, _) =>
                key == "super"
                    ? ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal("alice", "Alice", []))
                    : ValueTask.FromResult<TraxPrincipal?>(null),
            opts => opts.HeaderName = "X-My-Key"
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-My-Key", "super");

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response.Should().NotBeNull();
        response!.Name.Should().Be("Alice");
    }

    #endregion

    #region Valid authentication

    [Test]
    public async Task ValidHeader_ReturnsSuccess_WithPrincipalIdClaim()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal("alice", "Alice", ["User"]))
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response.Should().NotBeNull();
        response!.PrincipalId.Should().Be("alice");
    }

    [Test]
    public async Task ValidHeader_UserIdentityNameMatchesDisplayName()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(
                    new TraxPrincipal("alice", "Alice Liddell", ["User"])
                )
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response!.Name.Should().Be("Alice Liddell");
    }

    [Test]
    public async Task ValidHeader_RolesMappedToRoleClaims()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(
                    new TraxPrincipal("admin", "admin", ["Admin", "Player"])
                )
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response!.Roles.Should().BeEquivalentTo("Admin", "Player");
    }

    [Test]
    public async Task ValidHeader_CustomClaimBagFlowsThrough()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(
                    new TraxPrincipal(
                        "alice",
                        "Alice",
                        ["User"],
                        Claims: new Dictionary<string, string> { ["tenant"] = "acme" }
                    )
                )
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");

        response!.TenantClaim.Should().Be("acme");
    }

    #endregion

    #region Resolver failure modes

    [Test]
    public async Task ResolverReturnsNull_ForKnownKey_ReturnsFail()
    {
        using var host = await CreateHost((_, _) => ValueTask.FromResult<TraxPrincipal?>(null));
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "revoked");

        var response = await client.GetFromJsonAsync<AuthResultResponse>("/authresult");

        response!.Succeeded.Should().BeFalse();
        response.None.Should().BeFalse();
    }

    [Test]
    public async Task ResolverThrows_ReturnsFail_NotServerError()
    {
        using var host = await CreateHost(
            (_, _) => throw new InvalidOperationException("resolver crash")
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        var response = await client.GetAsync("/authresult");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResultResponse>();
        body!.Succeeded.Should().BeFalse();
        body.FailureMessage.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Concurrency

    [Test]
    public async Task ConcurrentRequests_EachResolvesIndependently()
    {
        var principals = new Dictionary<string, TraxPrincipal>
        {
            ["alice-key"] = new("alice", "Alice", ["User"]),
            ["bob-key"] = new("bob", "Bob", ["User"]),
            ["charlie-key"] = new("charlie", "Charlie", ["User"]),
        };

        using var host = await CreateHost(
            (key, _) =>
                principals.TryGetValue(key, out var p)
                    ? ValueTask.FromResult<TraxPrincipal?>(p)
                    : ValueTask.FromResult<TraxPrincipal?>(null)
        );

        var tasks = Enumerable
            .Range(0, 60)
            .Select(async i =>
            {
                var key = (i % 3) switch
                {
                    0 => "alice-key",
                    1 => "bob-key",
                    _ => "charlie-key",
                };
                var expectedId = (i % 3) switch
                {
                    0 => "alice",
                    1 => "bob",
                    _ => "charlie",
                };

                var client = host.GetTestClient();
                client.DefaultRequestHeaders.Add("X-Api-Key", key);
                var response = await client.GetFromJsonAsync<ProtectedResponse>("/protected");
                return (expected: expectedId, actual: response?.PrincipalId);
            });

        var results = await Task.WhenAll(tasks);

        foreach (var (expected, actual) in results)
            actual.Should().Be(expected);
    }

    #endregion

    #region DuplicateHeaders

    [Test]
    public async Task TwoApiKeyHeaders_Fails_ResolverNotInvoked()
    {
        var invocationCount = 0;
        using var host = await CreateHost(
            (_, _) =>
            {
                Interlocked.Increment(ref invocationCount);
                return ValueTask.FromResult<TraxPrincipal?>(
                    new TraxPrincipal("alice", "Alice", ["User"])
                );
            }
        );
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/authresult");
        request.Headers.Add("X-Api-Key", "key-one");
        request.Headers.Add("X-Api-Key", "key-two");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthResultResponse>();

        body!.Succeeded.Should().BeFalse();
        body.None.Should().BeFalse();
        body.FailureMessage.Should().Contain("Multiple API keys");
        invocationCount.Should().Be(0);
    }

    [Test]
    public async Task TwoApiKeyHeaders_ProtectedEndpoint_Returns401()
    {
        using var host = await CreateHost(
            (_, _) =>
                ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal("alice", "Alice", ["User"]))
        );
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Add("X-Api-Key", "key-one");
        request.Headers.Add("X-Api-Key", "key-two");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CommaJoinedHeader_Rejected_ResolverNotInvoked()
    {
        // RFC 7230 §3.2.2: some reverse proxies coalesce duplicate headers into a
        // single comma-joined value. That value passes the Count > 1 check as a
        // single entry. The handler must reject it so ambiguous credentials are
        // never handed to the resolver.
        var invocationCount = 0;
        using var host = await CreateHost(
            (_, _) =>
            {
                Interlocked.Increment(ref invocationCount);
                return ValueTask.FromResult<TraxPrincipal?>(
                    new TraxPrincipal("alice", "Alice", ["User"])
                );
            }
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "key-one,key-two");

        var response = await client.GetFromJsonAsync<AuthResultResponse>("/authresult");

        response!.Succeeded.Should().BeFalse();
        response.None.Should().BeFalse();
        response.FailureMessage.Should().Contain("Ambiguous");
        invocationCount.Should().Be(0);
    }

    [Test]
    public async Task SingleApiKeyHeader_StillAuthenticates()
    {
        // Regression guard so the duplicate-header check doesn't accidentally
        // reject a single legitimate header value.
        using var host = await CreateHost(
            (key, _) =>
                key == "good"
                    ? ValueTask.FromResult<TraxPrincipal?>(new TraxPrincipal("alice", "Alice", []))
                    : ValueTask.FromResult<TraxPrincipal?>(null)
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "good");

        var response = await client.GetAsync("/protected");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region ResolverLogging

    [Test]
    public async Task ResolverThrows_LogsAtWarning_NotError()
    {
        var loggerProvider = new TestLoggerProvider();
        using var host = await CreateHost(
            (_, _) => throw new InvalidOperationException("boom"),
            loggerProvider: loggerProvider
        );
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "any");

        await client.GetAsync("/authresult");

        loggerProvider.Entries.Should().NotBeEmpty();
        loggerProvider
            .Entries.Should()
            .NotContain(
                e => e.Level == LogLevel.Error,
                "resolver exceptions should log at Warning"
            );
        loggerProvider
            .Entries.Should()
            .Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("resolver"));
    }

    #endregion

    private sealed class DelegateResolver(
        Func<string, CancellationToken, ValueTask<TraxPrincipal?>> resolver
    ) : ITraxPrincipalResolver<string>
    {
        public ValueTask<TraxPrincipal?> ResolveAsync(string input, CancellationToken ct) =>
            resolver(input, ct);
    }

    private sealed class ProtectedResponse
    {
        public string? Name { get; set; }
        public string? PrincipalId { get; set; }
        public string[] Roles { get; set; } = [];
        public string? TenantClaim { get; set; }
    }

    private sealed class AuthResultResponse
    {
        public bool Succeeded { get; set; }
        public bool None { get; set; }
        public string? FailureMessage { get; set; }
    }

    private sealed class TestLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, Entries);

        public void Dispose() { }

        public record LogEntry(
            string Category,
            LogLevel Level,
            string Message,
            Exception? Exception
        );

        private sealed class TestLogger(string category, ConcurrentQueue<LogEntry> entries)
            : ILogger
        {
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            )
            {
                entries.Enqueue(
                    new LogEntry(category, logLevel, formatter(state, exception), exception)
                );
            }

            private sealed class NullScope : IDisposable
            {
                public static NullScope Instance { get; } = new();

                public void Dispose() { }
            }
        }
    }
}
