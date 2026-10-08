using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

/// <summary>
/// Prepares a branch's own dependency injection scope from the run's, for state a scoped service
/// holds that a fresh scope would not have: a tenant, a unit of work, anything set imperatively
/// on a scoped object rather than read from an ambient context.
/// </summary>
/// <remarks>
/// Every registered initializer runs, in registration order, before the branch's first step. One
/// that throws fails the branch: a branch that cannot see what the run sees does not run.
/// </remarks>
[Experimental(ExperimentalIds.Parallel)]
public interface IBranchScopeInitializer
{
    /// <summary>Copies what the branch needs from the run's scope into the branch's.</summary>
    /// <param name="run">The run's scope.</param>
    /// <param name="branch">The branch's new scope.</param>
    /// <param name="cancellationToken">The branch's token.</param>
    Task Initialize(
        IServiceProvider run,
        IServiceProvider branch,
        CancellationToken cancellationToken
    );
}

#pragma warning disable TRAXEXP001 // The implementation of the experimental feature itself.
public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// The path of the branch this monad runs, or null for a run's own monad.
    /// </summary>
    internal string? BranchPath { get; private set; }

    /// <summary>
    /// The scopes branches of this run opened, disposed when the run ends rather than at the join,
    /// because what a branch put in Memory may still hold on to its scope.
    /// </summary>
    internal List<AsyncServiceScope> BranchScopes { get; private set; } = [];

    /// <summary>
    /// Runs a fixed set of branches side by side, each on its own copy of Memory, and joins them
    /// before the next step.
    /// </summary>
    /// <remarks>
    /// <para>Each branch starts on the thread pool with a copy of Memory as it was at this step,
    /// its own dependency injection scope and its own cancellation token. When every branch has
    /// finished, the types each one added to Memory are merged into the run's Memory. Two
    /// branches adding the same type is refused when the chain is read; at run time a type two
    /// branches both added (an interface of a tuple element, say) is left out of the merge rather
    /// than taken from one of them.</para>
    /// <para>A branch that fails fails the step with a <see cref="BranchesFailedException"/>
    /// carrying every branch that failed. Under <see cref="BranchFailurePolicy.CancelSiblings"/>
    /// the others are cancelled first. A cancelled run fails with the cancellation.</para>
    /// <para>Branches compute; the step after the join commits. Writes that must be
    /// all-or-nothing belong after the join, because each branch has its own scope and so its
    /// own transaction.</para>
    /// </remarks>
    [Experimental(ExperimentalIds.Parallel)]
    public MonadTask<TInput, TReturn> Parallel(
        Func<Branches<TInput, TReturn>, Branches<TInput, TReturn>> branches
    )
    {
        var declared = branches(new Branches<TInput, TReturn>());

        return Recorder is not null ? RecordParallel(declared) : new(ParallelAsync(declared));
    }

    private async Task<Monad<TInput, TReturn>> ParallelAsync(Branches<TInput, TReturn> declared)
    {
        if (Exception is not null)
            return this;

        var step = Nodes.Next(
            ChainNodeScope.KeyOf(new ChainStep(ChainStepKind.Parallel, null, null, null))
        );
        var train = Train.GetType().ReadableName();

        // A declaration the startup check refuses is refused here too, for a host that skips it.
        if (declared.AllProblems.ToList() is { Count: > 0 } problems)
        {
            Exception = new TrainException(
                $"Parallel (train '{train}') {string.Join(" ", problems)}"
            );
            return this;
        }

        CancellationToken.ThrowIfCancellationRequested();

        using var siblings = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);
        var outcomes = new BranchOutcome[declared.Declared.Count];
        var failedFirst = 0;

        var children = new List<Monad<TInput, TReturn>>(declared.Declared.Count);
        foreach (var branch in declared.Declared)
            children.Add(await Fork(step, branch.Name, siblings.Token).ConfigureAwait(false));

        var running = declared
            .Declared.Select(
                (branch, i) =>
                    Task.Run(() => RunBranch(children[i], branch, i), CancellationToken.None)
            )
            .ToArray();

        // RunBranch never throws, so every task completes and each outcome is read on its own.
        await Task.WhenAll(running).ConfigureAwait(false);

        return Join(step, declared.Declared, children, outcomes);

        async Task RunBranch(
            Monad<TInput, TReturn> child,
            DeclaredBranch<TInput, TReturn> branch,
            int i
        )
        {
            RunningBranch.Enter(Train, child.CancellationToken);
            Exception? failure;

            try
            {
                if (child.Exception is null)
                    await branch
                        .Body(new MonadTask<TInput, TReturn>(Task.FromResult(child)))
                        .ConfigureAwait(false);

                failure = child.Exception;
            }
            catch (Exception e)
            {
                failure = e;
            }

            if (failure is null)
                return;

            // Whether the siblings had already been cancelled when this branch failed decides
            // whether its cancellation is its own or the sibling's.
            var siblingsCancelled = siblings.IsCancellationRequested;
            outcomes[i] = new BranchOutcome(
                failure,
                Interlocked.Increment(ref failedFirst),
                siblingsCancelled
            );

            if (declared.Policy == BranchFailurePolicy.CancelSiblings && !siblingsCancelled)
                await siblings.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A child monad for one branch: Memory as it is now, its own scope and token, and its steps
    /// numbered within the branch.
    /// </summary>
    private async Task<Monad<TInput, TReturn>> Fork(
        string step,
        string name,
        CancellationToken token
    )
    {
        var child = new Monad<TInput, TReturn>(Train, token)
        {
            Memory = new Dictionary<Type, object>(Memory),
            Nodes = Nodes.Track(step, name),
            BranchPath = $"{step}/{name}",
            BranchScopes = BranchScopes,
        };

        foreach (var (key, count) in _askings)
            child._askings[key] = count;
        foreach (var (key, type) in _askedAbout)
            child._askedAbout[key] = type;

        if (Memory.GetValueOrDefault(typeof(IServiceProvider)) is not IServiceProvider run)
            return child;

        if (run.GetService(typeof(IServiceScopeFactory)) is not IServiceScopeFactory scopes)
            return child;

        var scope = scopes.CreateAsyncScope();
        lock (BranchScopes)
            BranchScopes.Add(scope);

        child.Memory[typeof(IServiceProvider)] = scope.ServiceProvider;

        try
        {
            foreach (var initializer in run.GetServices<IBranchScopeInitializer>())
                await initializer
                    .Initialize(run, scope.ServiceProvider, token)
                    .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            child.Exception = new TrainException(
                $"Parallel branch '{child.BranchPath}' (train '{Train.GetType().ReadableName()}') "
                    + $"could not prepare its scope, so it does not run: {e.Message}"
            );
        }

        return child;
    }

    private Monad<TInput, TReturn> Join(
        string step,
        IReadOnlyList<DeclaredBranch<TInput, TReturn>> declared,
        IReadOnlyList<Monad<TInput, TReturn>> children,
        BranchOutcome[] outcomes
    )
    {
        // A cancelled run fails with the cancellation, never with its branches' failures.
        CancellationToken.ThrowIfCancellationRequested();

        var failed = outcomes
            .Select((o, i) => (Outcome: o, Path: children[i].BranchPath!))
            .Where(f => f.Outcome is not null)
            .OrderBy(f => f.Outcome!.Order)
            .ToList();

        if (failed.Count > 0)
        {
            // A cancellation that came before any sibling was cancelled was asked for from
            // outside the step, by the run's cancel flag or a timeout: the run was cancelled.
            if (
                failed.FirstOrDefault(f =>
                    f.Outcome!.Failure is OperationCanceledException && !f.Outcome.SiblingsCancelled
                ) is
                { Outcome: { } asked }
            )
                throw asked.Failure;

            var failures = failed
                .Where(f =>
                    !(
                        f.Outcome!.Failure is OperationCanceledException
                        && f.Outcome.SiblingsCancelled
                    )
                )
                .Select(f => new FailedBranch(
                    f.Path,
                    f.Outcome!.Failure,
                    FailureClassification.Carried(f.Outcome.Failure)
                ))
                .ToList();

            var stopped = failed
                .Where(f =>
                    f.Outcome!.Failure is OperationCanceledException && f.Outcome.SiblingsCancelled
                )
                .Select(f => f.Path)
                .ToList();

            Exception = Failed(step, failures, stopped);
            return this;
        }

        Merge(children);
        return this;
    }

    private BranchesFailedException Failed(
        string step,
        IReadOnlyList<FailedBranch> failures,
        IReadOnlyList<string> stopped
    )
    {
        var failure = new BranchesFailedException(step, failures, stopped);
        var first = failures[0];
        var firstData = first.Exception.Data["TrainExceptionData"] as TrainExceptionData;

        failure.Data["TrainExceptionData"] = new TrainExceptionData
        {
            TrainName = Train.GetType().Name,
            TrainExternalId = Train.ExternalId,
            Junction = $"{first.Branch}:{firstData?.Junction ?? first.Exception.GetType().Name}",
            Type = nameof(BranchesFailedException),
            Message = failure.Message,
            StackTrace = first.Exception.StackTrace,
            FailureClass = BranchesFailedException.Combine(failures.Select(f => f.FailureClass)),
        };

        return failure;
    }

    /// <summary>
    /// Puts what the branches added to Memory into the run's Memory. What a branch decided and
    /// which track it took stay the branch's. A type two branches both added is left out: neither
    /// value is the run's, and the startup check refuses the declared collisions, so only an
    /// undeclared one (an interface of a tuple element) reaches here.
    /// </summary>
    private void Merge(IReadOnlyList<Monad<TInput, TReturn>> children)
    {
        var added = new Dictionary<Type, object?>();

        foreach (var child in children)
        foreach (var (type, value) in child.Memory)
        {
            if (type == typeof(IServiceProvider) || !ChainVerification.Merged(type))
                continue;

            if (Memory.TryGetValue(type, out var before) && ReferenceEquals(before, value))
                continue;

            // Seen in an earlier branch: a collision, kept as a marker so a third does not win.
            added[type] = added.ContainsKey(type) ? null : value;
        }

        foreach (var (type, value) in added)
            if (value is not null)
                Memory[type] = value;
    }

    private sealed record BranchOutcome(Exception Failure, int Order, bool SiblingsCancelled);

    private MonadTask<TInput, TReturn> RecordParallel(Branches<TInput, TReturn> declared)
    {
        var recorder = Recorder!;

        foreach (var problem in declared.AllProblems)
            recorder.RefuseStep(ChainStepKind.Parallel, null, $"Parallel {problem}");

        var index = recorder.Steps.Count;
        recorder.Record(ChainStepKind.Parallel, null, null, null);

        var recorded = new List<ChainTrack>();

        foreach (var branch in declared.Declared)
        {
            var steps = RecordBranch(branch);

            foreach (var refusal in steps.RecordedRefusals)
                recorder.RefuseRecordedStep(
                    index,
                    ChainStepKind.Parallel,
                    refusal.Junction,
                    $"Parallel, branch '{branch.Name}': {refusal.Reason}"
                );

            recorded.Add(new ChainTrack(branch.Name, null, false, steps));
        }

        recorder.RecordTracks(index, recorded);

        // A junction instance carries its run's state (its result, its token), so two branches
        // running one instance would overwrite each other's.
        var handedTo = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (var branch in recorded)
        foreach (
            var instance in branch.Steps.Instances().Distinct(ReferenceEqualityComparer.Instance)
        )
            if (!handedTo.TryAdd(instance, branch.Name))
                recorder.RefuseRecordedStep(
                    index,
                    ChainStepKind.Parallel,
                    instance.GetType(),
                    $"Parallel: branches '{handedTo[instance]}' and '{branch.Name}' are handed the "
                        + $"same {instance.GetType().ReadableName()} instance, which holds the state "
                        + "of the step it runs. Give each branch its own instance, or chain the "
                        + "junction by type."
                );

        return new MonadTask<TInput, TReturn>(Task.FromResult(this));
    }

    /// <summary>
    /// Records one branch's body into a recorder of its own that knows it is a branch, so steps
    /// a branch cannot hold are refused where they are declared.
    /// </summary>
    private ChainRecorder RecordBranch(DeclaredBranch<TInput, TReturn> branch)
    {
        var outer = Recorder;
        var steps = new ChainRecorder(outer!) { InBranch = true };
        Recorder = steps;

        try
        {
            _ = branch.Body(new MonadTask<TInput, TReturn>(Task.FromResult(this)));
        }
        finally
        {
            Recorder = outer;
        }

        return steps;
    }
}
#pragma warning restore TRAXEXP001
