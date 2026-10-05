using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.Auth.E2E.Fixtures;
using Trax.Samples.Auth.E2E.Utilities;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Auth.E2E.Tests;

/// <summary>
/// <c>GateOperations(roles: Operator)</c> gates the <c>operations</c> namespace, reads and
/// mutations, while the rest of the endpoint stays open. An enqueue through it also applies the
/// queued train's own <c>[TraxAuthorize]</c>, so the operator role alone does not let Oscar queue
/// a train he could not run.
/// </summary>
[TestFixture]
public class OperationsGateTests : AuthTestFixture
{
    private const string Health = "{ operations { health { status } } }";

    private const string Hosts = "{ operations { hosts { instanceId environment } } }";

    private static string QueueTrain(string train, string inputJson) =>
        $$"""
            mutation {
              operations {
                workQueue {
                  queueTrain(input: { trainName: "Trax.Samples.Auth.Trains.{{train}}", inputJson: {{JsonString(
                inputJson
            )}} }) {
                    success id message
                  }
                }
              }
            }
            """;

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    [Test]
    public async Task OperationsReads_AreRefusedWithoutTheOperatorRole()
    {
        foreach (var query in new[] { Health, Hosts })
        foreach (var caller in new[] { Anonymous, BobKey, AliceKey, AliceToken, ErinToken })
        {
            var result = await GraphQL.SendAsync(query, caller);

            result.IsRefused.Should().BeTrue($"{caller} on {query}: {result.Raw}");
        }
    }

    [Test]
    public async Task OperationsReads_AreServedToAnOperator_OverEitherScheme()
    {
        foreach (var caller in new[] { OscarKey, OscarToken })
        {
            var result = await GraphQL.SendAsync(Health, caller);

            result.HasErrors.Should().BeFalse($"{caller}: {result.Raw}");
            result.GetData("operations", "health", "status").GetString().Should().Be("Healthy");
        }
    }

    [Test]
    public async Task OperationsMutations_AreRefusedWithoutTheOperatorRole()
    {
        foreach (var caller in new[] { Anonymous, BobToken, AliceKey })
        {
            var result = await GraphQL.SendAsync(
                QueueTrain("IEchoTrain", """{"message":"nope"}"""),
                caller
            );

            result.IsRefused.Should().BeTrue($"{caller}: {result.Raw}");
        }
    }

    [Test]
    public async Task Operator_QueuesATrainHeMayRun()
    {
        var result = await GraphQL.SendAsync(
            QueueTrain("IEchoTrain", """{"message":"queued by oscar"}"""),
            OscarToken
        );

        result.HasErrors.Should().BeFalse(result.Raw);
        result
            .GetData("operations", "workQueue", "queueTrain", "success")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task Operator_CannotQueueATrainHisRolesDoNotAllow()
    {
        var result = await GraphQL.SendAsync(
            QueueTrain("IPublishArticleTrain", """{"title":"x","body":"y"}"""),
            OscarKey
        );

        result
            .IsRefused.Should()
            .BeTrue("queueTrain applies the train's own [TraxAuthorize] on top of the gate");

        using var scope = SharedAuthSetup.Factory.Services.CreateScope();
        using var trax = (IDataContext)
            scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();
        (await trax.WorkQueues.CountAsync(w => w.TrainName.Contains("IPublishArticleTrain")))
            .Should()
            .Be(0, "a refused enqueue writes nothing");
    }

    [Test]
    public async Task Operator_SeesExecutionsOfEveryCaller()
    {
        // Other tests leave Oscar's own echo runs behind. Only a run newer than this marker can
        // be the anonymous caller's.
        var marker = await NewestEchoExecutionAsync() ?? 0;

        var echo = await GraphQL.SendAsync(
            """{ discover { echo(input: { message: "seen" }) { echoed } } }""",
            Anonymous
        );
        echo.HasErrors.Should().BeFalse(echo.Raw);

        var seen = await Polling.WaitUntilAsync(
            async () => await NewestEchoExecutionAsync() > marker,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(100)
        );

        seen.Should().BeTrue("an operator reads the execution history of every caller's runs");
    }

    /// <summary>The id of the newest echo execution the operator can list, or <c>null</c>.</summary>
    private async Task<long?> NewestEchoExecutionAsync()
    {
        var result = await GraphQL.SendAsync(
            """{ operations { executions(take: 1, trainName: "Trax.Samples.Auth.Trains.IEchoTrain") { items { id } } } }""",
            OscarKey
        );
        result.HasErrors.Should().BeFalse(result.Raw);
        var items = result.GetData("operations", "executions", "items");
        return items.GetArrayLength() == 0 ? null : items[0].GetProperty("id").GetInt64();
    }

    [Test]
    public async Task AnOperator_CannotReadAGatedNote_FromAnExecutionsSavedInput()
    {
        var note = $"gated note {Guid.NewGuid():N}";
        var published = await GraphQL.SendAsync(
            $$"""
            mutation { dispatch { news { publishArticle(input: { title: "t", body: "b", editorNote: "{{note}}" }) { externalId } } } }
            """,
            AliceToken
        );
        published.HasErrors.Should().BeFalse(published.Raw);
        var externalId = published
            .GetData("dispatch", "news", "publishArticle", "externalId")
            .GetString();

        // The operations view lists the run; its detail is what the operator role may read.
        long? id = null;
        var listed = await Polling.WaitUntilAsync(
            async () =>
            {
                var result = await GraphQL.SendAsync(
                    """{ operations { executions(take: 50, trainName: "Trax.Samples.Auth.Trains.IPublishArticleTrain") { items { id externalId } } } }""",
                    OscarKey
                );
                if (result.HasErrors)
                    return false;
                id = result
                    .GetData("operations", "executions", "items")
                    .EnumerateArray()
                    .Where(i => i.GetProperty("externalId").GetString() == externalId)
                    .Select(i => (long?)i.GetProperty("id").GetInt64())
                    .FirstOrDefault();
                return id is not null;
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(100)
        );
        listed.Should().BeTrue("the operator sees the run itself");

        var detail = await GraphQL.SendAsync(
            $$"""{ operations { executionDetail(id: {{id}}) { input output } } }""",
            OscarKey
        );

        detail.HasErrors.Should().BeFalse(detail.Raw);
        detail
            .Raw.Should()
            .NotContain(
                note,
                "the editor note is gated to editors and auditors, and the operations gate "
                    + "admits operators; a saved input would hand it to every operator"
            );
    }
}
