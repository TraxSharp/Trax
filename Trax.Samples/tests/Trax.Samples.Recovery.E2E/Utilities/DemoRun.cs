using System.Text.Json;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// Drives one demo run over GraphQL the way the page does: start it, find each attempt among the
/// manifest's executions, subscribe to its steps, and read the stored steps back.
/// </summary>
public sealed class DemoRun(GraphQLClient graphQL, JunctionEventStream stream, string apiKey)
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    public string RunId { get; private set; } = "";
    public long ManifestId { get; private set; }
    public string ManifestExternalId { get; private set; } = "";
    public List<long> Attempts { get; } = [];

    /// <summary>Starts a run through the sample's <c>startRun</c> mutation.</summary>
    public async Task StartAsync(string scenario, bool crashOnce, string? orderId = null)
    {
        var order = orderId is null ? "" : $", orderId: \"{orderId}\"";
        var response = await graphQL.SendAsync(
            $$"""
            mutation {
              dispatch {
                startRun(input: { scenario: {{scenario}}, crashOnce: {{(
                crashOnce ? "true" : "false"
            )}}{{order}} }) {
                  output { runId manifestId manifestExternalId }
                }
              }
            }
            """,
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var output = response.GetData("dispatch", "startRun", "output");
        RunId = output.GetProperty("runId").GetString()!;
        ManifestId = output.GetProperty("manifestId").GetInt64();
        ManifestExternalId = output.GetProperty("manifestExternalId").GetString()!;
    }

    /// <summary>
    /// Waits for the manifest's <paramref name="attempt"/>th execution to exist and subscribes to its
    /// steps at once. Returns its metadata id.
    /// </summary>
    public async Task<long> FollowAttemptAsync(int attempt)
    {
        long id = 0;
        var found = await Polling.WaitUntilAsync(
            async () =>
            {
                var ids = await ExecutionIdsAsync();
                if (ids.Count < attempt)
                    return false;
                id = ids[attempt - 1];
                return true;
            },
            Patience,
            TimeSpan.FromMilliseconds(40)
        );
        found.Should().BeTrue($"attempt {attempt} of manifest {ManifestId} should start");
        await stream.SubscribeAsync(id);
        Attempts.Add(id);
        return id;
    }

    /// <summary>The manifest's executions, oldest first.</summary>
    public async Task<List<long>> ExecutionIdsAsync()
    {
        var response = await graphQL.SendAsync(
            $$"""
            { operations { executions(manifestId: {{ManifestId}}, order: OLDEST) { items { id } } } }
            """,
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response
            .GetData("operations", "executions", "items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetInt64())
            .ToList();
    }

    /// <summary>Waits until the execution has finished and returns its state.</summary>
    public async Task<string> WaitForEndAsync(long metadataId)
    {
        var state = "";
        var ended = await Polling.WaitUntilAsync(
            async () =>
            {
                var response = await graphQL.SendAsync(
                    $$"""{ operations { execution(id: {{metadataId}}) { trainState } } }""",
                    apiKey
                );
                state = response.GetData("operations", "execution", "trainState").GetString()!;
                return state is "COMPLETED" or "FAILED" or "CANCELLED";
            },
            Patience,
            TimeSpan.FromMilliseconds(100)
        );
        ended.Should().BeTrue($"execution {metadataId} should finish");
        return state;
    }

    /// <summary>The stored steps of the execution, read through <c>operations.junctionRuns</c>.</summary>
    public async Task<List<JsonElement>> StoredStepsAsync(long metadataId)
    {
        var response = await graphQL.SendAsync(
            $$"""
            { operations { junctionRuns(metadataId: {{metadataId}}) {
                position kind name state questionKey answer replayed nameWithheld trackPosition attempt
                nodeId
            } } }
            """,
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("operations", "junctionRuns").EnumerateArray().ToList();
    }

    /// <summary>
    /// The run's timeline as the page builds it: the live steps merged with the stored ones by
    /// position, keeping whichever is further along (rows trail the stream slightly).
    /// </summary>
    public async Task<List<JsonElement>> TimelineAsync(long metadataId)
    {
        // A finished run's last rows may still be on their way to the table.
        List<JsonElement> stored = [];
        var settled = await Polling.WaitUntilAsync(
            async () =>
            {
                stored = await StoredStepsAsync(metadataId);
                return stored.Count > 0
                    && stored.All(s => s.GetProperty("state").GetString() != "IN_PROGRESS");
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(100)
        );
        settled
            .Should()
            .BeTrue(
                $"execution {metadataId}'s stored steps should all have ended within 10 s, but "
                    + $"{stored.Count} were stored and "
                    + $"{stored.Count(s => s.GetProperty("state").GetString() == "IN_PROGRESS")} "
                    + "still say IN_PROGRESS"
            );

        var byPosition = stored.ToDictionary(s => s.GetProperty("position").GetInt32());
        foreach (var live in stream.StepsOf(metadataId))
        {
            var step = live.GetProperty("junction");
            var position = step.GetProperty("position").GetInt32();
            if (
                !byPosition.TryGetValue(position, out var known)
                || known.GetProperty("state").GetString() == "IN_PROGRESS"
            )
                byPosition[position] = step;
        }
        return byPosition.OrderBy(p => p.Key).Select(p => p.Value).ToList();
    }

    /// <summary>The live steps the subscription delivered for the execution.</summary>
    public IReadOnlyList<JsonElement> LiveSteps(long metadataId) =>
        stream.StepsOf(metadataId).Select(e => e.GetProperty("junction")).ToList();

    /// <summary>
    /// The run the work queue entry <paramref name="entryId"/> was dispatched as, once it is: a
    /// requeue or a resume answers with its entry, and is not one of the manifest's executions.
    /// </summary>
    public async Task<long> RunOfEntryAsync(long entryId)
    {
        long runId = 0;
        var dispatched = await Polling.WaitUntilAsync(
            async () =>
            {
                var entry = await graphQL.SendAsync(
                    $$"""{ operations { workQueue { workQueue(id: {{entryId}}) { metadataId } } } }""",
                    apiKey
                );
                var id = entry.GetData("operations", "workQueue", "workQueue", "metadataId");
                if (id.ValueKind != JsonValueKind.Number)
                    return false;
                runId = id.GetInt64();
                return true;
            },
            Patience,
            TimeSpan.FromMilliseconds(100)
        );
        dispatched.Should().BeTrue($"work queue entry {entryId} should be dispatched");
        return runId;
    }

    /// <summary>
    /// The execution drawn on its train's declared chain, through <c>operations.runGraph</c>: every
    /// node by id, with nodes on a track or in a branch listed beside the top-level ones.
    /// </summary>
    public async Task<(bool CanResume, Dictionary<string, JsonElement> Nodes)> RunGraphAsync(
        long metadataId
    )
    {
        const string node = "id kind junction state canResume checkpointed";
        var response = await graphQL.SendAsync(
            $$"""
            { operations { runGraph(metadataId: {{metadataId}}) {
                canResume
                nodes { {{node}} tracks { name nodes { {{node}} tracks { name nodes { {{node}} } } } } }
            } } }
            """,
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        var graph = response.GetData("operations", "runGraph");

        var nodes = new Dictionary<string, JsonElement>();
        void Add(JsonElement list)
        {
            foreach (var n in list.EnumerateArray())
            {
                nodes[n.GetProperty("id").GetString()!] = n;
                if (n.TryGetProperty("tracks", out var tracks))
                    foreach (var track in tracks.EnumerateArray())
                        if (track.TryGetProperty("nodes", out var inner))
                            Add(inner);
            }
        }
        Add(graph.GetProperty("nodes"));
        return (graph.GetProperty("canResume").GetBoolean(), nodes);
    }

    /// <summary>What decision recording wrote for the execution, through the sample's query.</summary>
    public async Task<JsonElement> JournalAsync(long metadataId)
    {
        var response = await graphQL.SendAsync(
            $$"""
            { discover { decisionJournal(input: { metadataId: {{metadataId}} }) {
                replayDecisionsOf replayAbandoned
                decisions { questionKey occurrence replayed replayRefused stateHash }
            } } }
            """,
            apiKey
        );
        response.HasErrors.Should().BeFalse(response.FirstErrorMessage);
        return response.GetData("discover", "decisionJournal");
    }
}
