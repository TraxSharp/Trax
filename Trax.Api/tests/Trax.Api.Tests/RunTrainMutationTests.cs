using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Trax.Api.Tests.Auth;
using Trax.Effect.Data.InMemory.Services.InMemoryContextFactory;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Mediator.Services.TrainExecution;
using static Trax.Api.Tests.OperationsFailureMaskingTests;

namespace Trax.Api.Tests;

/// <summary>
/// <c>operations.workQueue.runTrain</c>, the API's counterpart of the dashboard's Run dialog,
/// over the real <c>OperationsService.RunTrainAsync</c> they share. What a caller sees follows
/// the operations payload convention: a refusal is <c>success: false</c> with a message (errors
/// as data), a caller who may not run the train is the <c>TRAX_AUTHORIZATION</c> error, and a
/// failure of the server is a masked error.
///
/// <para>Enforces <c>docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md</c>.</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md")]
public class RunTrainMutationTests
{
    private const string Adr =
        "docs/adr/0028-an-operations-mutation-returns-a-refusal-and-throws-a-failure.md";

    private const string TrainName = "Trax.Api.Tests.OperationsFailureMaskingTests+IMaskedTrain";

    private IDataContextProviderFactory _factory = null!;

    [SetUp]
    public void SetUp() =>
        _factory = new InMemoryContextProviderFactory(
            new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot()
        );

    private static string RunTrain(string trainName, string inputJson) =>
        $$"""
        mutation {
          operations {
            workQueue {
              runTrain(input: { trainName: "{{trainName}}", inputJson: {{JsonSerializer.Serialize(
                inputJson
            )}} }) {
                success
                id
                count
                message
              }
            }
          }
        }
        """;

    private static JsonElement Payload(JsonDocument doc) =>
        doc
            .RootElement.GetProperty("data")
            .GetProperty("operations")
            .GetProperty("workQueue")
            .GetProperty("runTrain");

    private async Task<int> RunsWritten()
    {
        await using var db = await _factory.CreateDbContextAsync(default);
        return await db.Metadatas.CountAsync();
    }

    [Test]
    public async Task A_run_is_submitted_and_its_id_is_the_execution_id()
    {
        var submitter = new RecordingSubmitter();
        using var host = await StartHostAsync(
            execution: null,
            submitter,
            dataContextFactory: _factory
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            null,
            RunTrain(TrainName, """{"value":"now"}""")
        );

        var payload = Payload(doc);
        payload.GetProperty("success").GetBoolean().Should().BeTrue(doc.RootElement.ToString());
        var id = payload.GetProperty("id").GetInt64();
        submitter.Submitted.Should().Equal(id);
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Metadatas.SingleAsync()).Id.Should().Be(id, "the id is the run's metadata id");

        await host.StopAsync();
    }

    [Test]
    public async Task Invalid_input_is_a_failed_result_and_writes_no_run()
    {
        var submitter = new RecordingSubmitter();
        using var host = await StartHostAsync(
            execution: null,
            submitter,
            dataContextFactory: _factory
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            null,
            RunTrain(TrainName, "{not json")
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse("a refusal is returned in the payload, not thrown (" + Adr + ")");
        var payload = Payload(doc);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("message").GetString().Should().StartWith("Invalid InputJson");
        payload.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        submitter.Submitted.Should().BeEmpty();
        (await RunsWritten()).Should().Be(0);

        await host.StopAsync();
    }

    [Test]
    public async Task An_unknown_train_is_a_failed_result()
    {
        using var host = await StartHostAsync(
            execution: null,
            new RecordingSubmitter(),
            dataContextFactory: _factory
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            null,
            RunTrain("Trax.X.INothing", "{}")
        );

        Payload(doc).GetProperty("success").GetBoolean().Should().BeFalse();
        Payload(doc).GetProperty("message").GetString().Should().StartWith("Unknown train");
        (await RunsWritten()).Should().Be(0);

        await host.StopAsync();
    }

    [Test]
    public async Task A_caller_who_may_not_run_the_train_gets_TRAX_AUTHORIZATION_and_no_run()
    {
        var submitter = new RecordingSubmitter();
        using var host = await StartHostAsync(
            execution: null,
            submitter,
            Registration(requiredRoles: ["RunOperators"]),
            _factory
        );

        // Malformed input too: authorization runs first, so the caller learns nothing about it.
        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            null,
            RunTrain(TrainName, "{not json")
        );

        AdminOperationsAuthorizationTests
            .HasErrorCode(doc, "TRAX_AUTHORIZATION")
            .Should()
            .BeTrue(doc.RootElement.ToString());
        doc.RootElement.GetRawText().Should().NotContain("RunOperators");
        submitter.Submitted.Should().BeEmpty();
        (await RunsWritten()).Should().Be(0);

        await host.StopAsync();
    }

    [Test]
    public async Task A_submit_failure_is_masked_and_the_run_is_recorded_failed()
    {
        using var host = await StartHostAsync(
            execution: null,
            new RecordingSubmitter(new HttpRequestException("worker 10.0.0.5:8080 refused")),
            dataContextFactory: _factory
        );

        var doc = await AdminOperationsAuthorizationTests.PostAsync(
            host,
            null,
            RunTrain(TrainName, "{}")
        );

        doc.RootElement.TryGetProperty("errors", out var errors)
            .Should()
            .BeTrue(
                "the train was accepted and the server could not start it: a failure, not a "
                    + "refusal ("
                    + Adr
                    + ")"
            );
        errors[0].GetProperty("message").GetString().Should().Be("Unexpected Execution Error");
        doc.RootElement.GetRawText().Should().NotContain("10.0.0.5");
        await using var db = await _factory.CreateDbContextAsync(default);
        (await db.Metadatas.SingleAsync()).TrainState.Should().Be(TrainState.Failed);

        await host.StopAsync();
    }
}
