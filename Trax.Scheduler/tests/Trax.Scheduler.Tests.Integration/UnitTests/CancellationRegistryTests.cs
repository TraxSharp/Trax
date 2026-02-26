using Trax.Scheduler.Services.CancellationRegistry;
using FluentAssertions;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Unit tests for <see cref="CancellationRegistry"/>, the in-memory CTS registry
/// used for same-server instant workflow cancellation.
/// </summary>
[TestFixture]
public class CancellationRegistryTests
{
    private CancellationRegistry _registry;

    [SetUp]
    public void SetUp()
    {
        _registry = new CancellationRegistry();
    }

    #region Register Tests

    [Test]
    public void Register_NewEntry_DoesNotThrow()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act & Assert
        var act = () => _registry.Register(1, cts);
        act.Should().NotThrow();
    }

    [Test]
    public void Register_SameIdTwice_OverwritesPreviousEntry()
    {
        // Arrange
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        _registry.Register(1, cts1);

        // Act — overwrite with cts2
        _registry.Register(1, cts2);

        // Assert — TryCancel should cancel cts2, not cts1
        _registry.TryCancel(1);
        cts2.IsCancellationRequested.Should().BeTrue();
        cts1.IsCancellationRequested.Should().BeFalse();
    }

    #endregion

    #region Unregister Tests

    [Test]
    public void Unregister_ExistingEntry_RemovesIt()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _registry.Register(1, cts);

        // Act
        _registry.Unregister(1);

        // Assert — TryCancel should return false (not found)
        _registry.TryCancel(1).Should().BeFalse();
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public void Unregister_NonExistentEntry_DoesNotThrow()
    {
        // Act & Assert
        var act = () => _registry.Unregister(999);
        act.Should().NotThrow();
    }

    [Test]
    public void Unregister_DoesNotDisposeCts()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        _registry.Register(1, cts);

        // Act
        _registry.Unregister(1);

        // Assert — CTS should still be usable (not disposed)
        var act = () => cts.Cancel();
        act.Should().NotThrow();
        cts.Dispose();
    }

    #endregion

    #region TryCancel Tests

    [Test]
    public void TryCancel_RegisteredEntry_CancelsCtsAndReturnsTrue()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _registry.Register(1, cts);

        // Act
        var result = _registry.TryCancel(1);

        // Assert
        result.Should().BeTrue();
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void TryCancel_UnregisteredEntry_ReturnsFalse()
    {
        // Act
        var result = _registry.TryCancel(999);

        // Assert
        result.Should().BeFalse();
    }

    [Test]
    public void TryCancel_AfterUnregister_ReturnsFalse()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _registry.Register(1, cts);
        _registry.Unregister(1);

        // Act
        var result = _registry.TryCancel(1);

        // Assert
        result.Should().BeFalse();
        cts.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public void TryCancel_CalledTwice_SecondCallStillReturnsTrue()
    {
        // Arrange — CTS stays registered even after first cancel
        using var cts = new CancellationTokenSource();
        _registry.Register(1, cts);
        _registry.TryCancel(1);

        // Act
        var result = _registry.TryCancel(1);

        // Assert — CTS is still in the registry, Cancel() is idempotent
        result.Should().BeTrue();
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void TryCancel_PropagatesCancellationToLinkedTokens()
    {
        // Arrange — create a linked token that should also get cancelled
        using var cts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        _registry.Register(1, cts);

        // Act
        _registry.TryCancel(1);

        // Assert
        linkedCts.IsCancellationRequested.Should().BeTrue();
    }

    #endregion

    #region Multiple Entries Tests

    [Test]
    public void MultipleEntries_CancelOne_DoesNotAffectOthers()
    {
        // Arrange
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();
        _registry.Register(1, cts1);
        _registry.Register(2, cts2);
        _registry.Register(3, cts3);

        // Act — cancel only entry 2
        _registry.TryCancel(2);

        // Assert
        cts1.IsCancellationRequested.Should().BeFalse();
        cts2.IsCancellationRequested.Should().BeTrue();
        cts3.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public void MultipleEntries_UnregisterOne_OthersRemain()
    {
        // Arrange
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        _registry.Register(1, cts1);
        _registry.Register(2, cts2);

        // Act
        _registry.Unregister(1);

        // Assert
        _registry.TryCancel(1).Should().BeFalse();
        _registry.TryCancel(2).Should().BeTrue();
        cts2.IsCancellationRequested.Should().BeTrue();
    }

    #endregion

    #region Thread Safety Tests

    [Test]
    public void ConcurrentRegisterAndCancel_DoesNotThrow()
    {
        // Arrange & Act
        var tasks = Enumerable
            .Range(0, 200)
            .Select(
                i =>
                    Task.Run(() =>
                    {
                        var cts = new CancellationTokenSource();
                        _registry.Register(i, cts);
                        _registry.TryCancel(i);
                        _registry.Unregister(i);
                        cts.Dispose();
                    })
            )
            .ToArray();

        // Assert
        var act = () => Task.WaitAll(tasks);
        act.Should().NotThrow();
    }

    [Test]
    public void ConcurrentCancelSameId_DoesNotThrow()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _registry.Register(1, cts);

        // Act — 100 threads all try to cancel the same entry
        var tasks = Enumerable
            .Range(0, 100)
            .Select(_ => Task.Run(() => _registry.TryCancel(1)))
            .ToArray();

        // Assert
        var act = () => Task.WaitAll(tasks);
        act.Should().NotThrow();
        cts.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void ConcurrentRegisterOverwrite_DoesNotThrow()
    {
        // Act — 100 threads all register different CTS for the same ID
        var sources = new CancellationTokenSource[100];
        var tasks = Enumerable
            .Range(0, 100)
            .Select(i =>
            {
                sources[i] = new CancellationTokenSource();
                var cts = sources[i];
                return Task.Run(() => _registry.Register(1, cts));
            })
            .ToArray();

        var act = () => Task.WaitAll(tasks);
        act.Should().NotThrow();

        // Cleanup
        foreach (var s in sources)
            s.Dispose();
    }

    #endregion
}
