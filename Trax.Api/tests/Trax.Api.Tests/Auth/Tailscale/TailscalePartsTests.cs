using System.Security.Cryptography;
using AwesomeAssertions;
using Trax.Api.Auth.Tailscale;

namespace Trax.Api.Tests.Auth.Tailscale;

/// <summary>
/// The pieces the tailnet scheme decides with: the proxy secret's comparison, the decoding of Tailscale's header
/// values, and the login map.
///
/// <para>Guard for <c>Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md</c>.</para>
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0063-a-tailnet-identity-is-trusted-only-from-the-proxy-the-deploy-controls.md"
)]
public class TailscalePartsTests
{
    private const string Secret = "parts-proxy-secret-0123456789abcdefghijkl";

    [Test]
    public void The_secret_matches_itself() =>
        new TailscaleProxySecret(Secret).Matches(Secret).Should().BeTrue();

    [TestCase("")]
    [TestCase("p")]
    [TestCase("parts-proxy-secret-0123456789abcdefghijk")]
    [TestCase("parts-proxy-secret-0123456789abcdefghijkm")]
    [TestCase("parts-proxy-secret-0123456789abcdefghijkl ")]
    [TestCase("PARTS-PROXY-SECRET-0123456789ABCDEFGHIJKL")]
    public void Anything_else_does_not_match(string presented) =>
        new TailscaleProxySecret(Secret).Matches(presented).Should().BeFalse();

    /// <summary>
    /// Timing cannot be measured reliably in a test, so this pins how the comparison is made: both sides hashed to
    /// 32 bytes and compared with <see cref="CryptographicOperations.FixedTimeEquals"/>, never as strings.
    /// </summary>
    [Test]
    public void The_secret_is_compared_in_constant_time()
    {
        var source = File.ReadAllText(
            Path.Combine(SourceRoot(), "src", "Trax.Api.Auth.Tailscale", "TailscaleProxySecret.cs")
        );

        source.Should().Contain("CryptographicOperations.FixedTimeEquals(digest, _digest)");
        source.Should().Contain("SHA256.HashData");
        source.Should().NotContain("string.Equals").And.NotContain("SequenceEqual");
        var handler = File.ReadAllText(
            Path.Combine(SourceRoot(), "src", "Trax.Api.Auth.Tailscale", "TailscaleAuthHandler.cs")
        );
        handler.Should().Contain("new TailscaleProxySecret(secret).Matches(");
        handler.Should().NotContain("== secret").And.NotContain("Equals(secret");
    }

    [TestCase("alice@example.com", "alice@example.com")]
    [TestCase("=?utf-8?q?Ren=C3=A9_Dupont?=", "René Dupont")]
    [TestCase("=?UTF-8?Q?Ren=C3=A9?= =?utf-8?q?_Dupont?=", "René Dupont")]
    public void A_header_value_decodes_as_Tailscale_writes_it(string raw, string expected) =>
        TailscaleHeaderValue.Decode(raw).Should().Be(expected);

    [TestCase("=?utf-8?q?Ren=C3?=")]
    [TestCase("=?utf-8?q?Ren=Z1?=")]
    [TestCase("=?utf-8?q?Ren=C")]
    [TestCase("=?iso-8859-1?q?Ren=E9?=")]
    [TestCase("=?utf-8?b?UmVuw6k=?=")]
    [TestCase("=?utf-8?q?a b?=")]
    public void A_header_value_encoded_some_other_way_does_not_decode(string raw) =>
        TailscaleHeaderValue.Decode(raw).Should().BeNull();

    [Test]
    public void A_login_maps_to_its_roles_ignoring_case()
    {
        var logins = new TailscaleBuilder().Login("Alice@Example.com", "Admin").Build();

        logins.RolesOf("alice@example.com").Should().Equal("Admin");
        logins.RolesOf("bob@example.com").Should().BeNull();
    }

    [Test]
    public void Any_login_gets_the_default_roles_and_a_named_one_its_own()
    {
        var logins = new TailscaleBuilder()
            .Login("alice@example.com", "Admin")
            .AnyLogin("Reader")
            .Build();

        logins.RolesOf("alice@example.com").Should().Equal("Admin");
        logins.RolesOf("bob@example.com").Should().Equal("Reader");
    }

    [TestCase("a@example.com,b@example.com")]
    [TestCase(" a@example.com")]
    public void A_login_with_a_comma_or_space_is_refused(string login)
    {
        var act = () => new TailscaleBuilder().Login(login);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void A_login_mapped_twice_is_refused()
    {
        var act = () => new TailscaleBuilder().Login("a@example.com").Login("A@example.com");

        act.Should().Throw<ArgumentException>().WithMessage("*mapped twice*");
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trax.Api.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not find Trax.Api.slnx above the tests.");
    }
}
