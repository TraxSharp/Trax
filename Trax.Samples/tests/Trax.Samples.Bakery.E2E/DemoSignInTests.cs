using AwesomeAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Trax.Samples.Bakery.Api.Auth;
using Trax.Samples.Shared.Auth;
using Trax.Samples.Shared.Testing;
using Trax.StateMachine.Gherkin.Reqnroll;

namespace Trax.Samples.Bakery.E2E;

/// <summary>
/// The shared demo sign-in the Bakery uses: a person named in two scenarios is two users, a token carries the
/// sample's issuer and audience, and an issuer refuses a key without <c>do-not-use-in-production</c>.
/// </summary>
[TestFixture]
public class DemoSignInTests
{
    private static readonly FeatureActor Customer = new("customer", [BakeryRoles.Customer], false);

    [Test]
    public void The_same_person_in_two_scenarios_is_two_users()
    {
        var credential = DemoFeatureCredentials.PerPerson(DemoTokens.Issuer);

        var first = Subject(credential(Customer, "Ada", "Ordering bread/Paying/1"));
        var second = Subject(credential(Customer, "Ada", "Ordering bread/Paying/2"));
        var again = Subject(credential(Customer, "Ada", "Ordering bread/Paying/1"));

        first.Should().StartWith("Ada-");
        second.Should().StartWith("Ada-");
        first.Should().NotBe(second);
        again.Should().Be(first);
    }

    [Test]
    public void A_token_carries_the_samples_issuer_audience_and_role()
    {
        var token = new JsonWebToken(DemoTokens.Issuer.Mint("Ada", BakeryRoles.Baker));

        token.Issuer.Should().Be("trax-samples-bakery-dev");
        token.Audiences.Should().Equal("trax-samples-bakery");
        token.GetClaim("role").Value.Should().Be(BakeryRoles.Baker);
        token.GetClaim("sub").Value.Should().Be("Ada");
    }

    [Test]
    public void A_role_the_sample_does_not_have_is_refused() =>
        FluentActions
            .Invoking(() => DemoTokens.Issuer.Mint("Ada", "admin"))
            .Should()
            .Throw<ArgumentException>();

    [Test]
    public void A_key_without_the_marker_is_refused() =>
        FluentActions
            .Invoking(() =>
                new DemoTokenIssuer(
                    "issuer",
                    "audience",
                    "a-perfectly-ordinary-signing-key-of-forty-bytes",
                    [BakeryRoles.Customer]
                )
            )
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*do-not-use-in-production*");

    private static string Subject(FeatureCredential credential) =>
        new JsonWebToken(credential.Headers["Authorization"]["Bearer ".Length..]).Subject;
}
