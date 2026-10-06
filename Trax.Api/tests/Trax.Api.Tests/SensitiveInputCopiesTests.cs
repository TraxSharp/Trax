using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// A <c>[TraxSensitive]</c> member is masked in every copy of a train input a read returns, and a
/// run whose recorded input was masked is never re-queued with the mask in place of the value.
/// A work queue entry and a manifest keep their input unmasked because a run starts from it, so
/// the reads mask those copies themselves, with the scheduler's <c>TransportInputRedaction</c>, whose
/// own tests are in Trax.Scheduler.
/// </summary>
[TestFixture]
public class SensitiveInputCopiesTests
{
    private const string Marker = """{"_redacted":true}""";

    private IDataContextProviderFactory _factory = null!;
    private ITrainDiscoveryService _discovery = null!;

    // The detail resolver reads through the operations service, as a host registers it.
    private IOperationsService Operations =>
        new OperationsService(
            _discovery,
            _factory,
            new SchedulerConfiguration(),
            Substitute.For<ITrainExecutionService>()
        );

    [SetUp]
    public void SetUp()
    {
        _factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        _discovery = Substitute.For<ITrainDiscoveryService>();
        _discovery
            .DiscoverTrains()
            .Returns([
                Registration(typeof(PaymentInput)),
                Registration(typeof(UntypedPaymentInput)),
                Registration(typeof(OpenEndedInput)),
                Registration(typeof(NestedUntypedInput)),
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
    public async Task Requeueing_a_run_whose_recorded_input_was_masked_is_refused()
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var meta = Metadata.Create(
                new CreateMetadata
                {
                    Name = typeof(IPaymentTrain).FullName!,
                    ExternalId = Guid.NewGuid().ToString("N"),
                    Input = null,
                }
            );
            meta.Input = $$"""{"accountId":"acct-1","cardNumber":{{Marker}}}""";
            await db.Track(meta);
            await db.SaveChanges(default);
            id = meta.Id;
        }
        var execution = Substitute.For<ITrainExecutionService>();
        var operations = new OperationsService(
            _discovery,
            _factory,
            new SchedulerConfiguration(),
            execution
        );

        var response = await new OperationsMutations().RequeueExecution(id, operations, default);

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("[TraxSensitive]");
        execution.ReceivedCalls().Should().BeEmpty("a masked input is refused before any enqueue");
    }

    [Test]
    public async Task A_work_queue_entrys_input_reads_with_its_sensitive_member_masked()
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = typeof(IPaymentTrain).FullName!,
                    Input = """{"accountId":"acct-1","cardNumber":"4111111111111111"}""",
                    InputTypeName = typeof(PaymentInput).FullName,
                }
            );
            await db.Track(entry);
            await db.SaveChanges(default);
            id = entry.Id;
        }

        var detail = await new WorkQueueQueries().GetDetail(id, Operations, default);

        var input = Parse(detail!.Input);
        input.GetProperty("accountId").GetString().Should().Be("acct-1");
        IsMarker(input.GetProperty("cardNumber")).Should().BeTrue(detail.Input);
        detail.Input.Should().NotContain("4111");
    }

    [Test]
    public async Task A_manifests_properties_read_with_their_sensitive_member_masked()
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var group = new ManifestGroup
            {
                Name = "payments",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await db.Track(group);
            await db.SaveChanges(default);
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(PaymentTrain) });
            manifest.ManifestGroupId = group.Id;
            manifest.PropertyTypeName = typeof(PaymentInput).FullName;
            manifest.Properties = """{"accountId":"acct-2","cardNumber":"5500000000000004"}""";
            await db.Track(manifest);
            await db.SaveChanges(default);
            id = manifest.Id;
        }

        var detail = await new OperationsQueries().GetManifestDetail(
            id,
            _factory,
            _discovery,
            default
        );

        var properties = Parse(detail!.Properties);
        properties.GetProperty("accountId").GetString().Should().Be("acct-2");
        IsMarker(properties.GetProperty("cardNumber")).Should().BeTrue(detail.Properties);
        detail.Properties.Should().NotContain("5500");
    }

    [Test]
    public async Task An_input_this_host_cannot_read_as_its_type_is_masked_whole()
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = "Other.App.IUnregisteredTrain",
                    Input = """{"secret":"s3cr3t"}""",
                    InputTypeName = "Other.App.UnregisteredInput",
                }
            );
            await db.Track(entry);
            await db.SaveChanges(default);
            id = entry.Id;
        }

        var detail = await new WorkQueueQueries().GetDetail(id, Operations, default);

        // Nothing shows the copy holds no sensitive member, so none of it is shown.
        IsMarker(Parse(detail!.Input)).Should().BeTrue(detail.Input);
        detail.Input.Should().NotContain("s3cr3t");
    }

    private async Task<string?> ReadWorkQueueInput(
        string json,
        Type inputType,
        string? typeName = null
    )
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = typeof(IPaymentTrain).FullName!,
                    Input = json,
                    InputTypeName = typeName ?? inputType.FullName,
                }
            );
            await db.Track(entry);
            await db.SaveChanges(default);
            id = entry.Id;
        }

        return (await new WorkQueueQueries().GetDetail(id, Operations, default))!.Input;
    }

    private async Task<string?> ReadManifestProperties(string json, Type inputType)
    {
        long id;
        await using (var db = await _factory.CreateDbContextAsync(default))
        {
            var group = new ManifestGroup
            {
                Name = "payments-" + Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            await db.Track(group);
            await db.SaveChanges(default);
            var manifest = Manifest.Create(new CreateManifest { Name = typeof(PaymentTrain) });
            manifest.ManifestGroupId = group.Id;
            manifest.PropertyTypeName = inputType.FullName;
            manifest.Properties = json;
            await db.Track(manifest);
            await db.SaveChanges(default);
            id = manifest.Id;
        }

        return (
            await new OperationsQueries().GetManifestDetail(id, _factory, _discovery, default)
        )!.Properties;
    }

    /// <summary>
    /// What the mediator stores for <c>new UntypedPaymentInput("acct-3", new Card("4111..."))</c>:
    /// it writes the runtime type, so the card number is in the stored copy.
    /// </summary>
    private static readonly string UntypedPaymentJson = JsonSerializer.Serialize(
        new UntypedPaymentInput("acct-3", new Card("4111111111111111", "Visa")),
        typeof(UntypedPaymentInput),
        Trax.Effect.Utils.TraxJsonSerializationOptions.ManifestProperties
    );

    [Test]
    public async Task A_member_typed_as_object_reads_masked_on_a_work_queue_entry()
    {
        UntypedPaymentJson.Should().Contain("4111", "the stored copy keeps the runtime value");

        var input = await ReadWorkQueueInput(UntypedPaymentJson, typeof(UntypedPaymentInput));

        var parsed = Parse(input);
        parsed.GetProperty("accountId").GetString().Should().Be("acct-3");
        IsMarker(parsed.GetProperty("details")).Should().BeTrue(input);
        input.Should().NotContain("4111").And.NotContain("Visa");
    }

    [Test]
    public async Task A_member_typed_as_object_reads_masked_on_a_manifest()
    {
        var properties = await ReadManifestProperties(
            UntypedPaymentJson,
            typeof(UntypedPaymentInput)
        );

        var parsed = Parse(properties);
        parsed.GetProperty("accountId").GetString().Should().Be("acct-3");
        IsMarker(parsed.GetProperty("details")).Should().BeTrue(properties);
        properties.Should().NotContain("4111");
    }

    [Test]
    public async Task Every_open_ended_member_reads_masked_and_typed_members_read_as_stored()
    {
        const string json = """
            {
              "name": "visible",
              "tags": ["a", "b"],
              "element": {"number": "4111111111111111"},
              "node": {"number": "4222222222222222"},
              "bag": {"card": {"number": "4333333333333333"}},
              "items": [{"number": "4444444444444444"}],
              "elements": {"k": {"number": "4555555555555555"}},
              "maybe": {"number": "4666666666666666"}
            }
            """;

        var input = await ReadWorkQueueInput(json, typeof(OpenEndedInput));

        var parsed = Parse(input);
        parsed.GetProperty("name").GetString().Should().Be("visible");
        parsed.GetProperty("tags").GetArrayLength().Should().Be(2);
        foreach (var member in new[] { "element", "node", "bag", "items", "elements", "maybe" })
            IsMarker(parsed.GetProperty(member)).Should().BeTrue($"{member} in {input}");
        input.Should().NotContainAny("4111", "4222", "4333", "4444", "4555", "4666");
    }

    [Test]
    public async Task An_open_ended_member_of_a_nested_type_reads_masked()
    {
        const string json = """
            {"holders": [{"label": "first", "payload": {"number": "4777777777777777"}}]}
            """;

        var input = await ReadManifestProperties(json, typeof(NestedUntypedInput));

        var holder = Parse(input).GetProperty("holders")[0];
        holder.GetProperty("label").GetString().Should().Be("first");
        IsMarker(holder.GetProperty("payload")).Should().BeTrue(input);
        input.Should().NotContain("4777");
    }

    [Test]
    public async Task An_assembly_qualified_type_name_reads_with_its_sensitive_member_masked()
    {
        var input = await ReadWorkQueueInput(
            """{"accountId":"acct-4","cardNumber":"4888888888888888"}""",
            typeof(PaymentInput),
            typeof(PaymentInput).AssemblyQualifiedName
        );

        IsMarker(Parse(input).GetProperty("cardNumber")).Should().BeTrue(input);
        Parse(input).GetProperty("accountId").GetString().Should().Be("acct-4");
    }

    [TestCase("Other.Assembly")]
    [TestCase("")]
    public async Task A_type_name_qualified_by_another_assembly_is_masked_whole(string assembly)
    {
        var input = await ReadWorkQueueInput(
            """{"accountId":"acct-5","cardNumber":"4999999999999999"}""",
            typeof(PaymentInput),
            $"{typeof(PaymentInput).FullName}, {assembly}"
        );

        IsMarker(Parse(input)).Should().BeTrue(input);
        input.Should().NotContain("acct-5");
    }

    [TestCase("X")]
    public async Task A_type_name_that_only_starts_with_a_registered_name_is_masked_whole(
        string suffix
    )
    {
        var input = await ReadWorkQueueInput(
            """{"accountId":"acct-6","cardNumber":"4000000000000002"}""",
            typeof(PaymentInput),
            typeof(PaymentInput).FullName + suffix
        );

        IsMarker(Parse(input)).Should().BeTrue(input);
    }

    [TestCase("null")]
    [TestCase("{not json")]
    [TestCase("""{"accountId": 12, "cardNumber": []}""")]
    public async Task A_stored_copy_that_does_not_read_as_its_type_is_masked_whole(string json)
    {
        var input = await ReadWorkQueueInput(json, typeof(PaymentInput));

        IsMarker(Parse(input)).Should().BeTrue(input);
    }

    [Test]
    public async Task A_stored_copy_with_no_type_name_is_masked_whole()
    {
        var input = await ReadWorkQueueInput(
            """{"accountId":"acct-7"}""",
            typeof(PaymentInput),
            typeName: ""
        );

        IsMarker(Parse(input)).Should().BeTrue(input);
    }

    internal sealed record Card([property: TraxSensitive] string Number, string Brand);

    internal sealed record UntypedPaymentInput(string AccountId, object Details);

    internal sealed record OpenEndedInput(
        string Name,
        List<string> Tags,
        JsonElement Element,
        System.Text.Json.Nodes.JsonNode Node,
        Dictionary<string, object> Bag,
        List<object> Items,
        IReadOnlyDictionary<string, JsonElement> Elements,
        JsonElement? Maybe
    );

    internal sealed record Holder(string Label, object Payload);

    internal sealed record NestedUntypedInput(IReadOnlyList<Holder> Holders);

    internal interface IPaymentTrain;

    internal sealed class PaymentTrain : IPaymentTrain;

    internal sealed record PaymentInput(
        string AccountId,
        [property: TraxSensitive] string CardNumber
    );
}
