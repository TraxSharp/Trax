using AwesomeAssertions;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Mediator.Tests.MemoryLeak.Integration.UnitTests;

[TestFixture]
public class TrustedExecutionScopeTests
{
    [Test]
    public void FreshScope_IsNotTrusted()
    {
        var scope = new TrustedExecutionScope();

        scope.IsTrusted.Should().BeFalse();
        scope.CurrentReason.Should().BeNull();
    }

    [Test]
    public void BeginTrusted_InsideBlock_IsTrusted()
    {
        var scope = new TrustedExecutionScope();

        using var _ = scope.BeginTrusted("test");

        scope.IsTrusted.Should().BeTrue();
        scope.CurrentReason.Should().Be("test");
    }

    [Test]
    public void BeginTrusted_AfterDispose_RevertsToUntrusted()
    {
        var scope = new TrustedExecutionScope();

        scope.BeginTrusted("test").Dispose();

        scope.IsTrusted.Should().BeFalse();
        scope.CurrentReason.Should().BeNull();
    }

    [Test]
    public void BeginTrusted_NestedScopes_InnerIsCurrent()
    {
        var scope = new TrustedExecutionScope();

        using var outer = scope.BeginTrusted("outer");
        scope.CurrentReason.Should().Be("outer");

        using (scope.BeginTrusted("inner"))
        {
            scope.CurrentReason.Should().Be("inner");
        }

        scope.CurrentReason.Should().Be("outer");
    }

    [Test]
    public void BeginTrusted_WhitespaceReason_Throws()
    {
        var scope = new TrustedExecutionScope();

        var act = () => scope.BeginTrusted("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void BeginTrusted_NullReason_Throws()
    {
        var scope = new TrustedExecutionScope();

        var act = () => scope.BeginTrusted(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task ScopeFlows_AcrossAwaitBoundary()
    {
        var scope = new TrustedExecutionScope();

        using var _ = scope.BeginTrusted("awaiting");
        await Task.Yield();
        await Task.Delay(5);

        scope.IsTrusted.Should().BeTrue();
        scope.CurrentReason.Should().Be("awaiting");
    }

    [Test]
    public async Task Scope_DoesNotLeak_AcrossIndependentTasks()
    {
        var scope = new TrustedExecutionScope();

        var trustedTask = Task.Run(async () =>
        {
            using var _ = scope.BeginTrusted("task-a");
            await Task.Delay(50);
            return (scope.IsTrusted, scope.CurrentReason);
        });

        var untrustedTask = Task.Run(async () =>
        {
            await Task.Delay(10);
            return (scope.IsTrusted, scope.CurrentReason);
        });

        var (trustedIsTrusted, trustedReason) = await trustedTask;
        var (untrustedIsTrusted, untrustedReason) = await untrustedTask;

        trustedIsTrusted.Should().BeTrue();
        trustedReason.Should().Be("task-a");
        untrustedIsTrusted.Should().BeFalse();
        untrustedReason.Should().BeNull();
    }

    [Test]
    public void Scope_DisposedTwice_Idempotent()
    {
        var scope = new TrustedExecutionScope();
        var handle = scope.BeginTrusted("test");

        handle.Dispose();
        handle.Dispose();

        scope.IsTrusted.Should().BeFalse();
    }

    [Test]
    public void OuterDisposedBeforeInner_EndsUntrustedWhenInnerIsDisposed()
    {
        var scope = new TrustedExecutionScope();
        var outer = scope.BeginTrusted("outer");
        var inner = scope.BeginTrusted("inner");

        outer.Dispose();

        scope.IsTrusted.Should().BeTrue("the inner scope is still open");
        scope.CurrentReason.Should().Be("inner");

        inner.Dispose();

        scope.IsTrusted.Should().BeFalse("both scopes are disposed");
        scope.CurrentReason.Should().BeNull();
    }

    [Test]
    public void MiddleDisposedFirst_IsSkippedWhenTheInnerPops()
    {
        var scope = new TrustedExecutionScope();
        var outer = scope.BeginTrusted("outer");
        var middle = scope.BeginTrusted("middle");
        var inner = scope.BeginTrusted("inner");

        middle.Dispose();
        scope.CurrentReason.Should().Be("inner");

        inner.Dispose();
        scope.CurrentReason.Should().Be("outer");

        outer.Dispose();
        scope.IsTrusted.Should().BeFalse();
    }

    [Test]
    public async Task UnawaitedTaskStartedInsideScope_IsUntrustedOnceTheScopeIsDisposed()
    {
        var scope = new TrustedExecutionScope();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool trustedBeforeDispose;
        Task<(bool IsTrusted, string? Reason)> captured;

        using (scope.BeginTrusted("captured"))
        {
            // Started inside the scope and not awaited there, so it captures the scope's flow.
            captured = Task.Run(async () =>
            {
                await release.Task;
                return (scope.IsTrusted, scope.CurrentReason);
            });
            trustedBeforeDispose = scope.IsTrusted;
        }

        release.SetResult();
        var (isTrusted, reason) = await captured;

        trustedBeforeDispose.Should().BeTrue();
        isTrusted
            .Should()
            .BeFalse("disposing the scope ends trust for every flow that captured it");
        reason.Should().BeNull();
    }

    [Test]
    public async Task UnawaitedTaskStartedInsideNestedScope_FallsBackToTheOpenOuterScope()
    {
        var scope = new TrustedExecutionScope();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(bool IsTrusted, string? Reason)> captured;

        using var outer = scope.BeginTrusted("outer");
        using (scope.BeginTrusted("inner"))
        {
            captured = Task.Run(async () =>
            {
                await release.Task;
                return (scope.IsTrusted, scope.CurrentReason);
            });
        }

        release.SetResult();
        var (isTrusted, reason) = await captured;

        isTrusted.Should().BeTrue("the outer scope the task also started inside is still open");
        reason.Should().Be("outer");
    }

    [Test]
    public void ScopeOpenedAfterAnOutOfOrderDispose_StillUnwindsToUntrusted()
    {
        var scope = new TrustedExecutionScope();
        var a = scope.BeginTrusted("a");
        var b = scope.BeginTrusted("b");
        a.Dispose();
        var c = scope.BeginTrusted("c");

        b.Dispose();
        scope.CurrentReason.Should().Be("c");

        c.Dispose();
        scope.IsTrusted.Should().BeFalse();
    }
}
