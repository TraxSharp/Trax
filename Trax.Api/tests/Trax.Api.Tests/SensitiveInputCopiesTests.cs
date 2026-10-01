using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
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
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// A <c>[TraxSensitive]</c> member is masked in every copy of a train input a read returns, and a
/// run whose recorded input was masked is never re-queued with the mask in place of the value.
/// A work queue entry and a manifest keep their input unmasked because a run starts from it, so
/// the reads mask those copies themselves.
/// </summary>
[TestFixture]
public class SensitiveInputCopiesTests
{
    private const string Marker = """{"_redacted":true}""";

    private IDataContextProviderFactory _factory = null!;
    private ITrainDiscoveryService _discovery = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new InMemoryContextProviderFactory(new InMemoryDatabaseRoot());
        _discovery = Substitute.For<ITrainDiscoveryService>();
        _discovery
            .DiscoverTrains()
            .Returns([
                new TrainRegistration
                {
                    ServiceType = typeof(IPaymentTrain),
                    ImplementationType = typeof(PaymentTrain),
                    InputType = typeof(PaymentInput),
                    OutputType = typeof(Unit),
                    Lifetime = ServiceLifetime.Scoped,
                    ServiceTypeName = nameof(IPaymentTrain),
                    ImplementationTypeName = nameof(PaymentTrain),
                    InputTypeName = nameof(PaymentInput),
                    OutputTypeName = nameof(Unit),
                    RequiredPolicies = [],
                    RequiredRoles = [],
                    IsQuery = false,
                    IsMutation = false,
                    IsRemote = false,
                    IsBroadcastEnabled = false,
                    GraphQLOperations = GraphQLOperation.Run,
                },
            ]);
    }

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
        var operations = Substitute.For<IOperationsService>();

        var response = await new OperationsMutations().RequeueExecution(
            id,
            _factory,
            operations,
            default
        );

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("[TraxSensitive]");
        await operations.DidNotReceiveWithAnyArgs().QueueTrainAsync(default!, default);
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

        var detail = await new WorkQueueQueries().GetDetail(id, _factory, _discovery, default);

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

        var detail = await new WorkQueueQueries().GetDetail(id, _factory, _discovery, default);

        // Nothing shows the copy holds no sensitive member, so none of it is shown.
        IsMarker(Parse(detail!.Input)).Should().BeTrue(detail.Input);
        detail.Input.Should().NotContain("s3cr3t");
    }

    internal interface IPaymentTrain;

    internal sealed class PaymentTrain : IPaymentTrain;

    internal sealed record PaymentInput(
        string AccountId,
        [property: TraxSensitive] string CardNumber
    );
}
