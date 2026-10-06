using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// The one masking the dashboard and the GraphQL API apply to the copies of a train input Trax
/// keeps in clear (a work queue entry's input, a manifest's properties): each
/// <c>[TraxSensitive]</c> member and each member whose declared type does not say what it holds
/// reads as the mask, and anything that cannot be read as a registered input type is masked whole.
/// </summary>
[TestFixture]
public class TransportInputRedactionTests
{
    private ITrainDiscoveryService _discovery = null!;

    [SetUp]
    public void SetUp()
    {
        _discovery = Substitute.For<ITrainDiscoveryService>();
        _discovery
            .DiscoverTrains()
            .Returns([
                Registration(typeof(PaymentInput)),
                Registration(typeof(UntypedPaymentInput)),
                Registration(typeof(ThrowingInput)),
            ]);
    }

    private static TrainRegistration Registration(Type inputType) =>
        new()
        {
            ServiceType = typeof(IPaymentTrain),
            ImplementationType = typeof(PaymentTrain),
            InputType = inputType,
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = nameof(IPaymentTrain),
            ImplementationTypeName = nameof(PaymentTrain),
            InputTypeName = inputType.Name,
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = false,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
        };

    private static JsonElement Parse(string? json) => JsonDocument.Parse(json!).RootElement;

    private static bool IsMarker(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Count() == 1
        && element.TryGetProperty("_redacted", out var flag)
        && flag.ValueKind == JsonValueKind.True;

    [Test]
    public void A_sensitive_member_is_masked_and_the_rest_reads_as_stored()
    {
        var masked = TransportInputRedaction.Redact(
            _discovery,
            """{"accountId":"acct-1","cardNumber":"4111111111111111"}""",
            typeof(PaymentInput).FullName
        );

        var parsed = Parse(masked);
        parsed.GetProperty("accountId").GetString().Should().Be("acct-1");
        IsMarker(parsed.GetProperty("cardNumber")).Should().BeTrue(masked);
        masked.Should().NotContain("4111");
    }

    [Test]
    public void A_member_typed_as_object_is_masked()
    {
        var masked = TransportInputRedaction.Redact(
            _discovery,
            """{"accountId":"acct-2","details":{"number":"4222222222222222"}}""",
            typeof(UntypedPaymentInput).FullName
        );

        IsMarker(Parse(masked).GetProperty("details")).Should().BeTrue(masked);
        masked.Should().NotContain("4222");
    }

    [TestCase("Other.App.UnregisteredInput")]
    [TestCase("")]
    [TestCase(null)]
    public void An_input_of_no_registered_type_is_masked_whole(string? typeName)
    {
        var masked = TransportInputRedaction.Redact(
            _discovery,
            """{"secret":"s3cr3t"}""",
            typeName
        );

        IsMarker(Parse(masked)).Should().BeTrue(masked);
    }

    [TestCase("null")]
    [TestCase("{not json")]
    public void A_copy_that_does_not_read_as_its_type_is_masked_whole(string json)
    {
        var masked = TransportInputRedaction.Redact(
            _discovery,
            json,
            typeof(PaymentInput).FullName
        );

        IsMarker(Parse(masked)).Should().BeTrue(masked);
    }

    [Test]
    public void A_type_whose_own_code_throws_while_it_is_read_is_masked_whole()
    {
        var masked = TransportInputRedaction.Redact(
            _discovery,
            """{"secret":"s3cr3t"}""",
            typeof(ThrowingInput).FullName
        );

        IsMarker(Parse(masked)).Should().BeTrue(masked);
    }

    [Test]
    public void No_copy_is_null() =>
        TransportInputRedaction
            .Redact(_discovery, null, typeof(PaymentInput).FullName)
            .Should()
            .BeNull();

    [Test]
    public void A_masked_open_ended_member_is_never_read_back_as_a_value()
    {
        var read = () =>
            JsonSerializer.Deserialize<UntypedPaymentInput>(
                """{"accountId":"acct-8","details":{"_redacted":true}}""",
                TransportInputRedaction.WriteOptions
            );

        read.Should().Throw<NotSupportedException>();
    }

    [Test]
    public void A_registration_whose_input_type_has_no_full_name_matches_no_stored_name()
    {
        var openParameter = typeof(List<>).GetGenericArguments()[0];
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration(openParameter)]);

        TransportInputRedaction.FindInputType(discovery, "T").Should().BeNull();
    }

    [TestCase(typeof(object), true)]
    [TestCase(typeof(JsonElement?), true)]
    [TestCase(typeof(System.Text.Json.Nodes.JsonArray), true)]
    [TestCase(typeof(JsonDocument), true)]
    [TestCase(typeof(System.Collections.ArrayList), true)]
    [TestCase(typeof(List<List<object>>), true)]
    [TestCase(typeof(IEnumerable<KeyValuePair<string, JsonElement>>), true)]
    [TestCase(typeof(string), false)]
    [TestCase(typeof(int?), false)]
    [TestCase(typeof(Card), false)]
    [TestCase(typeof(Dictionary<string, List<Card>>), false)]
    [TestCase(typeof(SelfNested), false)]
    public void Whether_a_member_type_can_hold_undeclared_members(Type type, bool openEnded) =>
        TransportInputRedaction.IsOpenEnded(type, depth: 0).Should().Be(openEnded);

    /// <summary>A collection whose element is itself: looked through a bounded number of times.</summary>
    internal sealed class SelfNested : IEnumerable<SelfNested>
    {
        public IEnumerator<SelfNested> GetEnumerator() =>
            Enumerable.Empty<SelfNested>().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }

    internal sealed record Card([property: TraxSensitive] string Number, string Brand);

    internal sealed record UntypedPaymentInput(string AccountId, object Details);

    /// <summary>An input type whose constructor throws something no JSON reader would.</summary>
    internal sealed class ThrowingInput
    {
        public ThrowingInput() => throw new InvalidOperationException("the type refuses");

        public string Secret { get; init; } = "";
    }

    internal interface IPaymentTrain;

    internal sealed class PaymentTrain : IPaymentTrain;

    internal sealed record PaymentInput(
        string AccountId,
        [property: TraxSensitive] string CardNumber
    );
}
