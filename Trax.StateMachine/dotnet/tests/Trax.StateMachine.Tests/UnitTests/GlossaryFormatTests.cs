using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Features;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.UnitTests;

/// <summary>
/// The glossary is read strictly (<c>Trax.Docs/adr/0058</c>): a version, no key it does not know, no actor without
/// roles unless it says any signed-in caller, no phrase two things could mean, and no phrase that speaks as "I", since
/// features name their people. Each of these was a way, in the
/// proof of concept, for a typo to open a trigger to everyone or send a step to the wrong machine.
/// </summary>
public class GlossaryFormatTests
{
    [Test]
    public void The_bakery_glossary_reads_into_its_actors_machines_and_views()
    {
        var glossary = Glossary.Read(Bakery().ToJsonString());

        glossary.Actors["customer"].Roles.Should().Equal("customer");
        glossary.Actors["customer"].Noun.Should().Be("a customer");
        var order = glossary.Machines["order"];
        order.OwnedBy.Should().Be("customer");
        order.Reference.Should().Be("order");
        order.Initial.Should().Be("Draft");
        order.Triggers["Order"].InputNames.Should().Equal("loaves");
        order.Guards["hasLoaves"].Fails.Should().Be("has nothing in it");
        order.Invokes["Paying"].Train.Should().Be("Bakery.Trains.ITakePayment");
        glossary.Machines["shop"].SystemKey.Should().Be("shop/main");
        glossary.Machines["shop"].Reference.Should().Be("the shop");
        glossary
            .Machines["receipt"]
            .Links["the receipt for {subject}"]
            .Field.Should()
            .Be("orderId");
        glossary
            .Views["the shop sign"]
            .Reads.Select(r => r.Machine)
            .Should()
            .Equal("order", "shop");
    }

    [Test]
    public void An_actor_with_no_roles_is_refused_naming_the_actor()
    {
        var glossary = Bakery();
        glossary["actors"]!["customer"]!.AsObject().Remove("roles");

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should().Throw<FormatException>().WithMessage("*/actors/customer*roles*");
    }

    [Test]
    public void An_actor_that_says_any_signed_in_caller_is_read_as_one()
    {
        var glossary = Bakery();
        var customer = glossary["actors"]!["customer"]!.AsObject();
        customer.Remove("roles");
        customer["anyAuthenticated"] = true;

        Glossary
            .Read(glossary.ToJsonString())
            .Actors["customer"]
            .AnyAuthenticated.Should()
            .BeTrue();
    }

    [Test]
    public void Two_triggers_sharing_a_sentence_are_refused_naming_both_places()
    {
        var glossary = Bakery();
        glossary["machines"]!["shop"]!["triggers"]!["Close"]!["phrase"] = "The  shop OPENS";

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage(
                "*/machines/shop/triggers/Close/phrase*/machines/shop/triggers/Open/phrase*"
            );
    }

    [Test]
    public void Two_machines_may_share_an_action_their_nouns_tell_apart()
    {
        var glossary = Bakery();
        glossary["machines"]!["order"]!["triggers"]!["Place"]!["phrase"] = "cancels {subject}";
        glossary["machines"]!["receipt"]!["triggers"]!["Cancel"]!["phrase"] = "cancels {subject}";

        var read = Glossary.Read(glossary.ToJsonString());

        read.Machines["receipt"].Triggers["Cancel"].Phrase.Should().Be("cancels {subject}");
    }

    [Test]
    public void A_phrase_that_speaks_as_I_is_refused()
    {
        var glossary = Bakery();
        glossary["machines"]!["order"]!["triggers"]!["Place"]!["phrase"] = "I place {subject}";

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should()
            .Throw<FormatException>()
            .WithMessage("*/triggers/Place/phrase*speaks as 'I'*name their people*");
    }

    [Test]
    public void A_users_trigger_that_does_not_say_where_its_machine_goes_is_refused()
    {
        var glossary = Bakery();
        glossary["machines"]!["order"]!["triggers"]!["Place"]!["phrase"] = "places the order";

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should().Throw<FormatException>().WithMessage("*/triggers/Place/phrase*{subject}*");
    }

    [Test]
    public void A_key_it_does_not_know_is_refused_naming_it()
    {
        var glossary = Bakery();
        glossary["actors"]!["customer"]!["role"] = "customer";

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should().Throw<FormatException>().WithMessage("*/actors/customer/role:*not a key*");
    }

    [Test]
    public void A_guard_with_no_rule_is_a_format_error_not_a_crash()
    {
        var glossary = Bakery();
        glossary["machines"]!["order"]!["guards"]!["hasLoaves"]!.AsObject().Remove("rule");

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should().Throw<FormatException>().WithMessage("*/guards/hasLoaves*'rule'*");
    }

    [Test]
    public void A_key_given_twice_in_one_object_is_refused()
    {
        var read = () =>
            Glossary.Read("""{ "version": 1, "version": 1, "actors": {}, "machines": {} }""");

        read.Should().Throw<FormatException>().WithMessage("glossary /: is not JSON*");
    }

    [Test]
    public void Another_version_is_refused()
    {
        var glossary = Bakery();
        glossary["version"] = 2;

        var read = () => Glossary.Read(glossary.ToJsonString());

        read.Should().Throw<FormatException>().WithMessage("*/version: must be 1*");
    }

    [TestCase("orders {loaves} loaves", new[] { "loaves" })]
    [TestCase("orders {loaves} loaves for {subject}", new[] { "loaves", "subject" })]
    [TestCase("orders loaves", new string[0])]
    public void A_trigger_phrase_names_its_placeholders(string phrase, string[] names)
    {
        Glossary.Placeholders(new At("glossary", ""), phrase).Should().Equal(names);
    }

    [TestCase("orders {loaves loaves")]
    [TestCase("orders loaves} now")]
    [TestCase("orders {} loaves")]
    [TestCase("orders {two words} loaves")]
    public void A_brace_that_is_no_placeholder_is_refused(string phrase)
    {
        var read = () => Glossary.Placeholders(new At("glossary", ""), phrase).ToList();

        read.Should().Throw<FormatException>();
    }

    private static JsonObject Bakery() =>
        JsonNode
            .Parse(
                File.ReadAllText(
                    Path.Combine(
                        FixturePaths.ConformanceRoot,
                        "formats",
                        "glossary",
                        "valid",
                        "bakery.json"
                    )
                )
            )!
            .AsObject();
}
