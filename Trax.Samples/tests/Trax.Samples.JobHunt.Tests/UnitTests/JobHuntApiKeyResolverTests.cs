using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.Auth;

namespace Trax.Samples.JobHunt.Tests.UnitTests;

[TestFixture]
public class JobHuntApiKeyResolverTests
{
    private JobHuntApiKeyResolver _resolver = null!;

    [SetUp]
    public void SetUp() => _resolver = new JobHuntApiKeyResolver();

    [Test]
    public async Task AliceKey_ResolvesAliceUserIdAndUserRole()
    {
        var principal = await _resolver.ResolveAsync(
            ApiKeyDefaults.AliceKey,
            CancellationToken.None
        );

        principal.Should().NotBeNull();
        principal!.Id.Should().Be("alice");
        principal.DisplayName.Should().Be("Alice");
        principal.Roles.Should().BeEquivalentTo(["User"]);
    }

    [Test]
    public async Task BobKey_ResolvesBobUserIdAndUserRole()
    {
        var principal = await _resolver.ResolveAsync(ApiKeyDefaults.BobKey, CancellationToken.None);

        principal.Should().NotBeNull();
        principal!.Id.Should().Be("bob");
        principal.DisplayName.Should().Be("Bob");
    }

    [Test]
    public async Task CharlieKey_ResolvesCharlieUserIdAndUserRole()
    {
        var principal = await _resolver.ResolveAsync(
            ApiKeyDefaults.CharlieKey,
            CancellationToken.None
        );

        principal.Should().NotBeNull();
        principal!.Id.Should().Be("charlie");
        principal.DisplayName.Should().Be("Charlie");
    }

    [Test]
    public async Task UnknownKey_ResolvesNull()
    {
        var principal = await _resolver.ResolveAsync("bogus-key", CancellationToken.None);

        principal.Should().BeNull();
    }

    [Test]
    public async Task PrincipalType_IsApiKey()
    {
        var principal = await _resolver.ResolveAsync(
            ApiKeyDefaults.AliceKey,
            CancellationToken.None
        );

        principal!.PrincipalType.Should().Be("apikey");
    }
}
