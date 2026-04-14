using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using Trax.Samples.JobHunt.Auth;

namespace Trax.Samples.JobHunt.Tests.UnitTests;

[TestFixture]
public class ApiKeyAuthHandlerTests
{
    private static async Task<AuthenticateResult> AuthenticateAsync(string? headerValue)
    {
        var optionsMonitor = new TestOptionsMonitor<AuthenticationSchemeOptions>(
            new AuthenticationSchemeOptions()
        );
        var loggerFactory = NullLoggerFactory.Instance;
        var encoder = UrlEncoder.Default;

        var handler = new ApiKeyAuthHandler(optionsMonitor, loggerFactory, encoder);

        var scheme = new AuthenticationScheme(
            ApiKeyDefaults.AuthenticationScheme,
            ApiKeyDefaults.AuthenticationScheme,
            typeof(ApiKeyAuthHandler)
        );

        var context = new DefaultHttpContext();
        if (headerValue != null)
            context.Request.Headers[ApiKeyDefaults.HeaderName] = headerValue;

        await handler.InitializeAsync(scheme, context);
        return await handler.AuthenticateAsync();
    }

    [Test]
    public async Task Authenticate_ValidAliceKey_AllowsRequestAndSetsClaims()
    {
        var result = await AuthenticateAsync(ApiKeyDefaults.AliceKey);

        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be("alice");
        result.Principal.FindFirstValue(ClaimTypes.Name).Should().Be("Alice");
        result.Principal.FindFirstValue(ClaimTypes.Role).Should().Be("User");
    }

    [Test]
    public async Task Authenticate_ValidBobKey_AllowsRequest()
    {
        var result = await AuthenticateAsync(ApiKeyDefaults.BobKey);

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be("bob");
    }

    [Test]
    public async Task Authenticate_ValidCharlieKey_AllowsRequest()
    {
        var result = await AuthenticateAsync(ApiKeyDefaults.CharlieKey);

        result.Succeeded.Should().BeTrue();
        result.Principal!.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be("charlie");
    }

    [Test]
    public async Task Authenticate_MissingHeader_Fails()
    {
        var result = await AuthenticateAsync(null);

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Contain(ApiKeyDefaults.HeaderName);
    }

    [Test]
    public async Task Authenticate_UnknownKey_Fails()
    {
        var result = await AuthenticateAsync("not-a-real-key");

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Contain("Invalid");
    }

    [Test]
    public async Task Authenticate_EmptyHeader_Fails()
    {
        var result = await AuthenticateAsync(string.Empty);

        result.Succeeded.Should().BeFalse();
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string> listener) => null;
    }
}
