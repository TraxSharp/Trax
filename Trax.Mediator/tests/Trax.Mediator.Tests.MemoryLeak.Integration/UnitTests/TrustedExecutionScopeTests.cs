using FluentAssertions;
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
}
