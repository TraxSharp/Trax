using System.Collections.Concurrent;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Trains, junctions and a store shared by the checkpoint tests: a research chain shaped like the
/// Recovery sample's (plan, route to a source, fetch, checkpoint, summarise), and the pieces a
/// test needs to see which steps ran.
/// </summary>
internal static class CheckpointFixtures
{
    public const string Adr =
        "Trax.Docs/adr/0047-a-checkpoint-stores-a-state-the-train-declares-and-a-resume-skips-to-it.md";

    [Asks("Which source?")]
    public enum Source
    {
        Web,
        Papers,
    }

    public sealed record Brief(string Topic);

    public sealed record Findings(string Topic, string Source);

    public sealed record Checked(string Topic, string Source, int Pages);

    public sealed record Report(string Text);

    /// <summary>Which junctions ran, in order, and the node each ran as.</summary>
    public sealed class Ran
    {
        public List<string> Junctions { get; } = [];

        public List<string> Nodes { get; } = [];

        public void Note(string junction)
        {
            lock (this)
            {
                Junctions.Add(junction);
                Nodes.Add(ChainGraph.CurrentNodeId ?? "");
            }
        }
    }

    /// <summary>Remembers every checkpoint a run took.</summary>
    public sealed class Store : ICheckpointStore
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _written = new();

        public List<CheckpointTaken> Taken { get; } = [];

        public Exception? Fails { get; set; }

        public Task Write(CheckpointTaken checkpoint, CancellationToken cancellationToken)
        {
            if (Fails is not null)
                throw Fails;

            lock (Taken)
                Taken.Add(checkpoint);

            WrittenTo(checkpoint.NodeId).TrySetResult();
            return Task.CompletedTask;
        }

        /// <summary>Completes once a checkpoint at <paramref name="nodeId"/> is stored.</summary>
        public Task Written(string nodeId) => WrittenTo(nodeId).Task;

        private TaskCompletionSource WrittenTo(string nodeId) =>
            _written.GetOrAdd(
                nodeId,
                _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
            );

        /// <summary>The checkpoint at <paramref name="nodeId"/>, read back as a resume restores it.</summary>
        public RestoredCheckpoint Restored(string nodeId)
        {
            var taken = Taken.Single(t => t.NodeId == nodeId);
            return new RestoredCheckpoint(taken.NodeId, taken.StateType, taken.State, taken.Tracks);
        }
    }

    /// <summary>A container holding whatever a test registers.</summary>
    public sealed class Services : IServiceProvider
    {
        private readonly Dictionary<Type, object> _registered = [];

        public Services With<T>(T service)
            where T : class
        {
            _registered[typeof(T)] = service;
            return this;
        }

        public object? GetService(Type serviceType) => _registered.GetValueOrDefault(serviceType);
    }

    /// <summary>A replay that answers nothing and notes every asking it was offered.</summary>
    public sealed class NotingReplay : IDecisionReplay
    {
        public List<(string Key, int Occurrence)> Asked { get; } = [];

        public Task<RecordedAnswer?> Replay(
            string train,
            string runId,
            string key,
            int occurrence,
            CancellationToken cancellationToken
        )
        {
            Asked.Add((key, occurrence));
            return Task.FromResult<RecordedAnswer?>(null);
        }
    }

    public sealed class PlanResearch(Ran ran) : Junction<string, Brief>
    {
        public override Task<Brief> Run(string input)
        {
            ran.Note(nameof(PlanResearch));
            return Task.FromResult(new Brief(input));
        }
    }

    public sealed class SearchWeb(Ran ran) : Junction<Brief, Findings>
    {
        public override Task<Findings> Run(Brief input)
        {
            ran.Note(nameof(SearchWeb));
            return Task.FromResult(new Findings(input.Topic, "web"));
        }
    }

    public sealed class SearchPapers(Ran ran) : Junction<Brief, Findings>
    {
        public override Task<Findings> Run(Brief input)
        {
            ran.Note(nameof(SearchPapers));
            return Task.FromResult(new Findings(input.Topic, "papers"));
        }
    }

    public sealed class FetchFullTexts(Ran ran) : Junction<Findings, Checked>
    {
        public override Task<Checked> Run(Findings input)
        {
            ran.Note(nameof(FetchFullTexts));
            return Task.FromResult(new Checked(input.Topic, input.Source, 12));
        }
    }

    /// <summary>Fails while <see cref="Failing"/> is set, as the sample's armed crash does.</summary>
    public sealed class Summarize(Ran ran, Crash crash) : Junction<Checked, Report>
    {
        public override Task<Report> Run(Checked input)
        {
            ran.Note(nameof(Summarize));

            if (crash.Failing)
                throw new TimeoutException("summariser timed out");

            return Task.FromResult(
                new Report($"{input.Topic} from {input.Source}, {input.Pages} pages")
            );
        }
    }

    public sealed class Crash
    {
        public bool Failing { get; set; }

        /// <summary>
        /// What the failing junction waits for before it throws, so a failure in one Parallel
        /// branch comes after its sibling reached a given point instead of cancelling it first.
        /// </summary>
        public Task? FailsAfter { get; set; }
    }

    /// <summary>
    /// <c>PlanResearch → Switch&lt;Brief, Source&gt; → FetchFullTexts → Checkpoint&lt;Checked&gt; →
    /// Summarize</c>, with the decider, store and replay a test hands it.
    /// </summary>
    public sealed class ResearchTrain(Ran ran, Crash crash, Services services)
        : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(services.With(ran).With(crash))
                .Chain<PlanResearch>()
                .Switch<Brief, Source>(s =>
                    s.When(Source.Web, w => w.Chain<SearchWeb>())
                        .When(Source.Papers, p => p.Chain<SearchPapers>())
                )
                .Chain<FetchFullTexts>()
                .Checkpoint<Checked>()
                .Chain<Summarize>()
                .Resolve();
    }

    /// <summary>The same chain with the checkpoint inside the Papers track.</summary>
    public sealed class CheckpointInATrackTrain(Ran ran, Crash crash, Services services)
        : Train<string, Report>
    {
        protected override Task<Either<Exception, Report>> Junctions() =>
            AddServices<IServiceProvider>(services.With(ran).With(crash))
                .Chain<PlanResearch>()
                .Switch<Brief, Source>(s =>
                    s.When(Source.Web, w => w.Chain<SearchWeb>().Chain<FetchFullTexts>())
                        .When(
                            Source.Papers,
                            p =>
                                p.Chain<SearchPapers>()
                                    .Chain<FetchFullTexts>()
                                    .Checkpoint<Checked>()
                        )
                )
                .Chain<Summarize>()
                .Resolve();
    }

    /// <summary>A sealed state with the members a checkpoint commonly holds.</summary>
    public sealed record Rich(
        string Name,
        int Count,
        Source Lane,
        List<string> Tags,
        Dictionary<string, int> Scores,
        Checked? Nested
    );

    public static Services With(ICheckpointStore store, IDecider decider) =>
        new Services().With(store).With(decider);
}
