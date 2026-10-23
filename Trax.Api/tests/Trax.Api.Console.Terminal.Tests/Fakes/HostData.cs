using System.Text.Json.Nodes;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>What a host answers the console's documents with, as small as the generated records accept.</summary>
internal static class HostData
{
    public static string Run(
        long id,
        string train,
        string state = "FAILED",
        string? reason = "card_declined"
    ) =>
        $$"""
            {"id":{{id}},"externalId":"run-{{id}}","name":"{{train}}","trainState":"{{state}}",
             "startTime":"2026-10-22T10:00:00Z","endTime":null,"failureJunction":{{(
                reason is null ? "null" : "\"ChargeCard\""
            )}},
             "failureReason":{{(
                reason is null ? "null" : $"\"{reason}\""
            )}},"manifestId":null,"hostName":"web-2",
             "failureClass":"UNCLASSIFIED","cancellationRequested":false,"parentId":null,"currentlyRunningJunction":null}
            """;

    public static string Runs(int total, long? next, params string[] rows) =>
        """{"operations":{"executions":{"items":["""
        + string.Join(",", rows)
        + $"],\"totalCount\":{total},\"isEstimatedCount\":false,\"nextCursor\":{Cursor(next)}}}}}}}";

    public static string DeadLetter(long id, string manifest) =>
        $$"""
            {"id":{{id}},"manifestId":3,"manifestName":"{{manifest}}","manifestExternalId":null,"status":"AWAITING_INTERVENTION",
             "deadLetteredAt":"2026-10-22T10:00:00Z","reason":"Retries exhausted","retryCountAtDeadLetter":3,
             "resolvedAt":null,"resolutionNote":null}
            """;

    public static string DeadLetters(int total, params string[] rows) =>
        """{"operations":{"deadLetters":{"deadLetters":{"items":["""
        + string.Join(",", rows)
        + $"],\"totalCount\":{total},\"isEstimatedCount\":false,\"nextCursor\":null}}}}}}}}";

    public const string NoQueue =
        """{"operations":{"workQueue":{"workQueues":{"items":[],"totalCount":0,"isEstimatedCount":false,"nextCursor":null}}}}""";

    public const string NoManifests =
        """{"operations":{"manifests":{"items":[],"totalCount":0,"isEstimatedCount":false,"nextCursor":null}}}""";

    public const string NoMachines = """{"operations":{"machines":[]}}""";

    /// <summary>A host whose lists answer: two failed runs, one dead letter, nothing else.</summary>
    public static FakeHost Console() =>
        new FakeHost()
            .Answer(
                "Executions",
                Runs(
                    2,
                    null,
                    Run(4812, "SendInvoice"),
                    Run(4807, "SyncCustomers", "COMPLETED", null)
                )
            )
            .Answer("DeadLetters", DeadLetters(1, DeadLetter(7, "NightlyBilling")))
            .Answer("WorkQueue", NoQueue)
            .Answer("Manifests", NoManifests)
            .Answer("Machines", NoMachines)
            .Answer(
                "AdminTrainNames",
                """{"operations":{"adminTrainNames":["Trax.Scheduler.Trains.JobRunner.IJobRunnerTrain"]}}"""
            );

    public static string Variables(JsonObject variables) => variables.ToJsonString();

    private static string Cursor(long? next) => next?.ToString() ?? "null";
}
