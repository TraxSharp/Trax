using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Trax.Effect.Attributes;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

/// <summary>
/// What <see cref="TraxRedaction.WithRedaction"/> masks, in every shape a train's input or output
/// can take. Each case serializes through the options Trax stores parameters with, and also
/// through plain options, because a host can pass its own to <c>SaveTrainParameters</c>.
///
/// <para>Enforces docs/adr/0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md")]
public class TraxRedactionTests
{
    private const string Secret = "hunter2";

    private static IEnumerable<JsonSerializerOptions> Options()
    {
        yield return TraxJsonSerializationOptions.Default;
        yield return TraxJsonSerializationOptions.JunctionLogging;
        yield return new JsonSerializerOptions();
    }

    private static string Serialize(object value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, value.GetType(), TraxRedaction.WithRedaction(options));

    [TestCaseSource(nameof(Options))]
    public void A_marked_property_is_masked_and_its_neighbours_are_not(
        JsonSerializerOptions options
    )
    {
        var json = Serialize(new Login { User = "ada", Password = Secret }, options);

        json.Should()
            .NotContain(
                Secret,
                "a [TraxSensitive] member is masked where its copy is written "
                    + "(docs/adr/0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md)"
            )
            .And.Contain("ada");
        TraxRedaction.ContainsRedaction(json).Should().BeTrue();
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_property_on_a_nested_object_is_masked(JsonSerializerOptions options)
    {
        var json = Serialize(
            new Order
            {
                Id = 7,
                Payment = new Card { Holder = "ada", Number = Secret },
            },
            options
        );

        json.Should().NotContain(Secret).And.Contain("ada");
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_property_inside_every_element_of_a_collection_is_masked(
        JsonSerializerOptions options
    )
    {
        var json = Serialize(
            new Batch
            {
                Cards =
                [
                    new Card { Holder = "ada", Number = Secret },
                    new Card { Holder = "bob", Number = Secret + "b" },
                ],
            },
            options
        );

        json.Should().NotContain(Secret).And.Contain("ada").And.Contain("bob");
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_collection_is_masked_whole(JsonSerializerOptions options)
    {
        var json = Serialize(new Keyring { Owner = "ada", Keys = [Secret, Secret + "2"] }, options);

        json.Should().NotContain(Secret).And.Contain("ada");
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_object_is_masked_whole_rather_than_walked(JsonSerializerOptions options)
    {
        var json = Serialize(
            new Wallet
            {
                Owner = "ada",
                Card = new Card { Holder = "bob", Number = "4111" },
            },
            options
        );

        json.Should()
            .NotContain("bob", "a marked property hides everything under it, marked or not")
            .And.NotContain("4111");
    }

    [TestCaseSource(nameof(Options))]
    public void A_positional_record_parameter_marked_without_a_target_is_masked(
        JsonSerializerOptions options
    )
    {
        var json = Serialize(new ApiCredential("ada", Secret), options);

        json.Should()
            .NotContain(Secret, "on a positional record the attribute lands on the parameter")
            .And.Contain("ada");
    }

    [TestCaseSource(nameof(Options))]
    public void A_positional_record_parameter_marked_for_its_property_is_masked(
        JsonSerializerOptions options
    )
    {
        var json = Serialize(new PropertyTargetedCredential("ada", Secret), options);

        json.Should().NotContain(Secret).And.Contain("ada");
    }

    [TestCaseSource(nameof(Options))]
    public void A_renamed_property_is_masked_under_its_json_name(JsonSerializerOptions options)
    {
        var json = Serialize(new Renamed { Token = Secret }, options);

        json.Should().NotContain(Secret);
        JsonNode.Parse(json)!["access_token"].Should().NotBeNull("the JSON name is kept");
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_field_is_masked(JsonSerializerOptions options)
    {
        var json = Serialize(new WithField { Pin = Secret }, options);

        json.Should().NotContain(Secret);
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_value_type_is_masked(JsonSerializerOptions options)
    {
        var json = Serialize(new WithNumber { Label = "x", Cvv = 987 }, options);

        json.Should().NotContain("987");
    }

    [TestCaseSource(nameof(Options))]
    public void An_override_of_a_marked_property_is_masked(JsonSerializerOptions options)
    {
        var json = Serialize(new DerivedLogin { Password = Secret }, options);

        json.Should().NotContain(Secret, "the mark is inherited by an override");
    }

    [TestCaseSource(nameof(Options))]
    public void A_property_marked_on_its_interface_is_masked(JsonSerializerOptions options)
    {
        var json = Serialize(new ImplementsSecret { Secret = Secret }, options);

        json.Should().NotContain(Secret);
    }

    [TestCaseSource(nameof(Options))]
    public void A_marked_object_behind_an_object_typed_property_is_masked(
        JsonSerializerOptions options
    )
    {
        var json = Serialize(
            new Envelope
            {
                Body = new Login { User = "ada", Password = Secret },
            },
            options
        );

        json.Should().NotContain(Secret).And.Contain("ada");
    }

    [Test]
    public void Nothing_is_masked_by_name_alone()
    {
        var json = Serialize(
            new Unmarked { Password = "visible", ApiKey = "visible-too" },
            new JsonSerializerOptions()
        );

        json.Should()
            .Contain("visible")
            .And.Contain(
                "visible-too",
                "masking is opt-in and a name is not a mark "
                    + "(docs/adr/0010-a-sensitive-field-is-marked-and-masked-where-it-is-written.md)"
            );
        TraxRedaction.ContainsRedaction(json).Should().BeFalse();
    }

    [Test]
    public void The_serialized_object_is_left_untouched()
    {
        var login = new Login { User = "ada", Password = Secret };

        Serialize(login, new JsonSerializerOptions());

        login.Password.Should().Be(Secret, "the train still runs with the real value");
    }

    [Test]
    public void The_same_options_give_the_same_redacting_options()
    {
        var options = new JsonSerializerOptions();

        TraxRedaction
            .WithRedaction(options)
            .Should()
            .BeSameAs(
                TraxRedaction.WithRedaction(options),
                "a fresh options instance per call would discard System.Text.Json's metadata cache"
            );
    }

    [Test]
    public void The_given_options_are_not_changed()
    {
        var options = new JsonSerializerOptions();
        TraxRedaction.WithRedaction(options);

        JsonSerializer
            .Serialize(new Login { User = "ada", Password = Secret }, options)
            .Should()
            .Contain(Secret, "only the derived options redact");
    }

    [Test]
    public void A_redacted_value_cannot_be_read_back_as_the_real_one()
    {
        var json = Serialize(new Login { User = "ada", Password = Secret }, new());

        var read = () => JsonSerializer.Deserialize<Login>(json);

        read.Should().Throw<JsonException>("the mask is not a password");
    }

    [Test]
    public void ContainsRedaction_is_false_for_json_without_the_marker() =>
        TraxRedaction
            .ContainsRedaction("""{"a":{"b":[1,{"_redacted":false}]}}""")
            .Should()
            .BeFalse();

    [Test]
    public void ContainsRedaction_is_false_for_text_that_is_not_json() =>
        TraxRedaction.ContainsRedaction("not json").Should().BeFalse();

    public class Login
    {
        public string User { get; set; } = "";

        [TraxSensitive]
        public string Password { get; set; } = "";
    }

    public class BaseLogin
    {
        [TraxSensitive]
        public virtual string Password { get; set; } = "";
    }

    public class DerivedLogin : BaseLogin
    {
        public override string Password { get; set; } = "";
    }

    public interface IHasSecret
    {
        [TraxSensitive]
        string Secret { get; }
    }

    public class ImplementsSecret : IHasSecret
    {
        public string Secret { get; set; } = "";
    }

    public class Card
    {
        public string Holder { get; set; } = "";

        [TraxSensitive]
        public string Number { get; set; } = "";
    }

    public class Order
    {
        public int Id { get; set; }
        public Card Payment { get; set; } = new();
    }

    public class Batch
    {
        public List<Card> Cards { get; set; } = [];
    }

    public class Keyring
    {
        public string Owner { get; set; } = "";

        [TraxSensitive]
        public List<string> Keys { get; set; } = [];
    }

    public class Wallet
    {
        public string Owner { get; set; } = "";

        [TraxSensitive]
        public Card Card { get; set; } = new();
    }

    public record ApiCredential(string ClientId, [TraxSensitive] string ClientSecret);

    public record PropertyTargetedCredential(
        string ClientId,
        [property: TraxSensitive] string ClientSecret
    );

    public class Renamed
    {
        [TraxSensitive]
        [JsonPropertyName("access_token")]
        public string Token { get; set; } = "";
    }

    public class WithField
    {
        [TraxSensitive]
        public string Pin = "";
    }

    public class WithNumber
    {
        public string Label { get; set; } = "";

        [TraxSensitive]
        public int Cvv { get; set; }
    }

    public class Envelope
    {
        public object? Body { get; set; }
    }

    public class Unmarked
    {
        public string Password { get; set; } = "";
        public string ApiKey { get; set; } = "";
    }
}
