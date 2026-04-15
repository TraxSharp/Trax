using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Trax.Api.Auth;
using Trax.Api.Auth.ApiKey;

namespace Trax.Api.Tests.Auth;

[TestFixture]
public class ApiKeyBuilderTests
{
    #region Add (cleartext, id + roles)

    [Test]
    public async Task Add_WithIdAndRoles_ResolvesToPrincipalWithIdAsDisplayName()
    {
        var builder = new ApiKeyBuilder().Add("k1", id: "alice", "User");
        var resolver = BuildInternal(builder);

        var principal = await resolver.ResolveAsync("k1", CancellationToken.None);

        principal.Should().NotBeNull();
        principal!.Id.Should().Be("alice");
        principal.DisplayName.Should().Be("alice");
        principal.Roles.Should().BeEquivalentTo(["User"]);
        principal.PrincipalType.Should().Be("apikey");
    }

    [Test]
    public async Task Add_WithNoRoles_ResolvesPrincipalWithEmptyRoleList()
    {
        var builder = new ApiKeyBuilder().Add("k1", id: "alice");
        var resolver = BuildInternal(builder);

        var principal = await resolver.ResolveAsync("k1", CancellationToken.None);

        principal!.Roles.Should().BeEmpty();
    }

    [Test]
    public void Add_WithNullKey_Throws()
    {
        var act = () => new ApiKeyBuilder().Add(null!, id: "alice");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Add_WithEmptyKey_Throws()
    {
        var act = () => new ApiKeyBuilder().Add("", id: "alice");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Add_WithWhitespaceKey_Throws()
    {
        var act = () => new ApiKeyBuilder().Add("   ", id: "alice");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Add_WithEmptyId_Throws()
    {
        var act = () => new ApiKeyBuilder().Add("k1", id: "");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task Add_RoleArrayIsSnapshotted_MutatingOriginalDoesNotAffectPrincipal()
    {
        var roles = new[] { "User" };
        var builder = new ApiKeyBuilder().Add("k1", id: "alice", roles);
        roles[0] = "Admin";

        var resolver = BuildInternal(builder);
        var principal = await resolver.ResolveAsync("k1", CancellationToken.None);

        principal!.Roles.Should().BeEquivalentTo(["User"]);
    }

    #endregion

    #region Add (cleartext, factory)

    [Test]
    public async Task Add_WithFactory_ResolvesToFactoryOutput()
    {
        var builder = new ApiKeyBuilder().Add(
            "k1",
            () => new TraxPrincipal("alice", "Alice Liddell", ["Admin"])
        );
        var resolver = BuildInternal(builder);

        var principal = await resolver.ResolveAsync("k1", CancellationToken.None);

        principal!.DisplayName.Should().Be("Alice Liddell");
    }

    [Test]
    public async Task Add_Factory_OnlyInvokedOnMatch()
    {
        var invoked = 0;
        var builder = new ApiKeyBuilder().Add(
            "k1",
            () =>
            {
                invoked++;
                return new TraxPrincipal("alice", "Alice", []);
            }
        );
        var resolver = BuildInternal(builder);

        await resolver.ResolveAsync("not-k1", CancellationToken.None);
        invoked.Should().Be(0);

        await resolver.ResolveAsync("k1", CancellationToken.None);
        invoked.Should().Be(1);
    }

    [Test]
    public void Add_WithNullFactory_Throws()
    {
        var act = () => new ApiKeyBuilder().Add("k1", (Func<TraxPrincipal>)null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region AddHashed

    [Test]
    public async Task AddHashed_MatchingPreHashedKey_Resolves()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes("secret")]);

        var builder = new ApiKeyBuilder().AddHashed(salt, hash, id: "alice", "Admin");
        var resolver = BuildInternal(builder);

        var principal = await resolver.ResolveAsync("secret", CancellationToken.None);

        principal.Should().NotBeNull();
        principal!.Id.Should().Be("alice");
        principal.Roles.Should().BeEquivalentTo(["Admin"]);
    }

    [Test]
    public void AddHashed_WrongHashLength_Throws()
    {
        var salt = new byte[16];
        var shortHash = new byte[16];

        var act = () => new ApiKeyBuilder().AddHashed(salt, shortHash, id: "alice");

        act.Should().Throw<ArgumentException>().WithMessage("*32 bytes*");
    }

    [Test]
    public void AddHashed_EmptySalt_Throws()
    {
        var salt = Array.Empty<byte>();
        var hash = new byte[32];

        var act = () => new ApiKeyBuilder().AddHashed(salt, hash, id: "alice");

        act.Should().Throw<ArgumentException>().WithMessage("*Salt*non-empty*");
    }

    [Test]
    public void AddHashed_NullSalt_Throws()
    {
        var act = () => new ApiKeyBuilder().AddHashed(null!, new byte[32], id: "alice");
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AddHashed_NullHash_Throws()
    {
        var act = () => new ApiKeyBuilder().AddHashed(new byte[16], null!, id: "alice");
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Build

    [Test]
    public void Build_NoEntries_ThrowsWithActionableMessage()
    {
        var builder = new ApiKeyBuilder();

        var act = () => BuildInternal(builder);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*AddTraxApiKeyAuth*at least one key*Add*AddHashed*");
    }

    [Test]
    public async Task Build_MultipleKeys_EachResolvesIndependently()
    {
        var builder = new ApiKeyBuilder()
            .Add("k-alice", id: "alice", "User")
            .Add("k-bob", id: "bob", "Admin");

        var resolver = BuildInternal(builder);

        var a = await resolver.ResolveAsync("k-alice", CancellationToken.None);
        var b = await resolver.ResolveAsync("k-bob", CancellationToken.None);
        var c = await resolver.ResolveAsync("k-charlie", CancellationToken.None);

        a!.Id.Should().Be("alice");
        b!.Id.Should().Be("bob");
        c.Should().BeNull();
    }

    [Test]
    public async Task Build_MixedCleartextAndPreHashed_BothResolve()
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = SHA256.HashData([.. salt, .. Encoding.UTF8.GetBytes("prehashed-key")]);

        var builder = new ApiKeyBuilder()
            .Add("cleartext-key", id: "alice")
            .AddHashed(salt, hash, id: "bob");
        var resolver = BuildInternal(builder);

        (await resolver.ResolveAsync("cleartext-key", CancellationToken.None))!
            .Id.Should()
            .Be("alice");
        (await resolver.ResolveAsync("prehashed-key", CancellationToken.None))!
            .Id.Should()
            .Be("bob");
    }

    #endregion

    // Build() is internal; tests in the same assembly reach it via the
    // Trax.Api.Auth.ApiKey InternalsVisibleTo grant to Trax.Api.Tests.
    private static HashedApiKeyResolver BuildInternal(ApiKeyBuilder builder) => builder.Build();
}
