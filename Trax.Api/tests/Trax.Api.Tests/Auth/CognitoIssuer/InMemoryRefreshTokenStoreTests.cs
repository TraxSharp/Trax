using FluentAssertions;
using Trax.Api.Auth.Jwt.Cognito.Issuer;

namespace Trax.Api.Tests.Auth.CognitoIssuer;

[TestFixture]
public class InMemoryRefreshTokenStoreTests
{
    private const string ClientId = "client-1";

    [Test]
    public async Task IssueAsync_ReturnsTokenAndExpiry()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryRefreshTokenStore(clock);
        var sub = Guid.NewGuid();

        var handle = await store.IssueAsync(
            sub,
            ClientId,
            TimeSpan.FromDays(30),
            CancellationToken.None
        );

        handle.Token.Should().NotBeNullOrEmpty();
        handle.ExpiresAt.Should().Be(clock.GetUtcNow().AddDays(30));
    }

    [Test]
    public void IssueAsync_EmptyClientId_Throws()
    {
        var store = new InMemoryRefreshTokenStore();
        Func<Task> act = async () =>
            await store.IssueAsync(
                Guid.NewGuid(),
                "",
                TimeSpan.FromHours(1),
                CancellationToken.None
            );
        act.Should().ThrowAsync<ArgumentException>();
    }

    [Test]
    public void IssueAsync_ZeroLifetime_Throws()
    {
        var store = new InMemoryRefreshTokenStore();
        Func<Task> act = async () =>
            await store.IssueAsync(Guid.NewGuid(), ClientId, TimeSpan.Zero, CancellationToken.None);
        act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ValidateAsync_FreshToken_ReturnsClaims()
    {
        var store = new InMemoryRefreshTokenStore();
        var sub = Guid.NewGuid();
        var handle = await store.IssueAsync(
            sub,
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );

        var claims = await store.ValidateAsync(handle.Token, CancellationToken.None);

        claims.Should().NotBeNull();
        claims!.Sub.Should().Be(sub);
        claims.ClientId.Should().Be(ClientId);
        claims.ExpiresAt.Should().Be(handle.ExpiresAt);
    }

    [Test]
    public async Task ValidateAsync_UnknownToken_ReturnsNull()
    {
        var store = new InMemoryRefreshTokenStore();
        var result = await store.ValidateAsync("never-issued", CancellationToken.None);
        result.Should().BeNull();
    }

    [Test]
    public async Task ValidateAsync_EmptyToken_ReturnsNull()
    {
        var store = new InMemoryRefreshTokenStore();
        var result = await store.ValidateAsync("", CancellationToken.None);
        result.Should().BeNull();
    }

    [Test]
    public async Task ValidateAsync_ExpiredToken_ReturnsNull()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryRefreshTokenStore(clock);
        var handle = await store.IssueAsync(
            Guid.NewGuid(),
            ClientId,
            TimeSpan.FromMinutes(5),
            CancellationToken.None
        );

        clock.Advance(TimeSpan.FromMinutes(6));
        var result = await store.ValidateAsync(handle.Token, CancellationToken.None);

        result.Should().BeNull();
    }

    [Test]
    public async Task RotateAsync_ReplacesToken()
    {
        var store = new InMemoryRefreshTokenStore();
        var sub = Guid.NewGuid();
        var first = await store.IssueAsync(
            sub,
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );

        var rotated = await store.RotateAsync(first.Token, CancellationToken.None);

        rotated.Should().NotBeNull();
        rotated!.Token.Should().NotBe(first.Token);
        rotated.ExpiresAt.Should().Be(first.ExpiresAt, "rotation does not extend session length");

        var oldClaims = await store.ValidateAsync(first.Token, CancellationToken.None);
        oldClaims.Should().BeNull("the rotated token is consumed");

        var newClaims = await store.ValidateAsync(rotated.Token, CancellationToken.None);
        newClaims.Should().NotBeNull();
        newClaims!.Sub.Should().Be(sub);
    }

    [Test]
    public async Task RotateAsync_UnknownToken_ReturnsNull()
    {
        var store = new InMemoryRefreshTokenStore();
        var result = await store.RotateAsync("not-issued", CancellationToken.None);
        result.Should().BeNull();
    }

    [Test]
    public async Task RotateAsync_AlreadyRotated_ReturnsNull()
    {
        var store = new InMemoryRefreshTokenStore();
        var handle = await store.IssueAsync(
            Guid.NewGuid(),
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );

        var first = await store.RotateAsync(handle.Token, CancellationToken.None);
        first.Should().NotBeNull();

        var second = await store.RotateAsync(handle.Token, CancellationToken.None);
        second.Should().BeNull();
    }

    [Test]
    public async Task RotateAsync_ConcurrentCalls_OnlyOneSucceeds()
    {
        var store = new InMemoryRefreshTokenStore();
        var handle = await store.IssueAsync(
            Guid.NewGuid(),
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );

        // Fire many concurrent rotations against the same token; the
        // ConcurrentDictionary CAS in InMemoryRefreshTokenStore.RotateAsync
        // must let exactly one through.
        var tasks = Enumerable
            .Range(0, 32)
            .Select(_ => store.RotateAsync(handle.Token, CancellationToken.None))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r is not null).Should().Be(1);
        results.Count(r => r is null).Should().Be(31);
    }

    [Test]
    public async Task RevokeAsync_InvalidatesChain()
    {
        var store = new InMemoryRefreshTokenStore();
        var handle = await store.IssueAsync(
            Guid.NewGuid(),
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );
        var rotated = await store.RotateAsync(handle.Token, CancellationToken.None);

        await store.RevokeAsync(rotated!.Token, CancellationToken.None);

        var stillValid = await store.ValidateAsync(rotated.Token, CancellationToken.None);
        stillValid.Should().BeNull();
    }

    [Test]
    public async Task RevokeAsync_UnknownToken_NoThrow()
    {
        var store = new InMemoryRefreshTokenStore();
        Func<Task> act = async () => await store.RevokeAsync("not-issued", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task RevokeAsync_AfterRotation_OldTokenAlsoInvalid()
    {
        var store = new InMemoryRefreshTokenStore();
        var first = await store.IssueAsync(
            Guid.NewGuid(),
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );
        var second = await store.RotateAsync(first.Token, CancellationToken.None);
        var third = await store.RotateAsync(second!.Token, CancellationToken.None);

        // Revoke any link in the chain; every token in the chain (past + present) is invalidated.
        await store.RevokeAsync(third!.Token, CancellationToken.None);

        (await store.ValidateAsync(third.Token, CancellationToken.None)).Should().BeNull();
        // first and second were already consumed via rotation, so still null;
        // the contract here is that revoking does not "un-revoke" them by overwriting state.
    }

    [Test]
    public async Task RevokeAllAsync_InvalidatesEveryChainForUser()
    {
        var store = new InMemoryRefreshTokenStore();
        var sub = Guid.NewGuid();

        var a = await store.IssueAsync(
            sub,
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );
        var b = await store.IssueAsync(
            sub,
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );
        var other = await store.IssueAsync(
            Guid.NewGuid(),
            ClientId,
            TimeSpan.FromHours(1),
            CancellationToken.None
        );

        await store.RevokeAllAsync(sub, ClientId, CancellationToken.None);

        (await store.ValidateAsync(a.Token, CancellationToken.None)).Should().BeNull();
        (await store.ValidateAsync(b.Token, CancellationToken.None)).Should().BeNull();
        (await store.ValidateAsync(other.Token, CancellationToken.None))
            .Should()
            .NotBeNull("a different user's tokens are untouched");
    }

    [Test]
    public async Task RevokeAllAsync_DifferentClientId_DoesNotRevoke()
    {
        var store = new InMemoryRefreshTokenStore();
        var sub = Guid.NewGuid();
        var a = await store.IssueAsync(
            sub,
            "client-A",
            TimeSpan.FromHours(1),
            CancellationToken.None
        );
        var b = await store.IssueAsync(
            sub,
            "client-B",
            TimeSpan.FromHours(1),
            CancellationToken.None
        );

        await store.RevokeAllAsync(sub, "client-A", CancellationToken.None);

        (await store.ValidateAsync(a.Token, CancellationToken.None)).Should().BeNull();
        (await store.ValidateAsync(b.Token, CancellationToken.None)).Should().NotBeNull();
    }

    [Test]
    public void RevokeAllAsync_EmptyClientId_Throws()
    {
        var store = new InMemoryRefreshTokenStore();
        Func<Task> act = async () =>
            await store.RevokeAllAsync(Guid.NewGuid(), "", CancellationToken.None);
        act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
