using FluentAssertions;
using Trax.Scheduler.Services.CancellationRegistry;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Unit tests for <see cref="CancellationRegistry"/>, the in-memory CTS registry
/// used for same-server instant train cancellation.
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
    public void Register_SameIdTwice_KeepsTheLiveRegistration()
    {
        // Arrange - a second delivery of a running job registers the same id
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        _registry.Register(1, cts1);

        // Act
        _registry.Register(1, cts2);

        // Assert - a cancel still reaches the job that is running
        _registry.TryCancel(1);
        cts1.IsCancellationRequested.Should().BeTrue();
        cts2.IsCancellationRequested.Should().BeFalse();
    }

    [Test]
    public void Unregister_WithAnotherCallersSource_LeavesTheRegistration()
    {
        using var live = new CancellationTokenSource();
        using var duplicate = new CancellationTokenSource();
        _registry.Register(1, live);
        _registry.Register(1, duplicate);

        _registry.Unregister(1, duplicate);

        _registry.TryCancel(1).Should().BeTrue();
        live.IsCancellationRequested.Should().BeTrue();
    }

    [Test]
    public void Unregister_WithItsOwnSource_RemovesTheRegistration()
    {
        using var live = new CancellationTokenSource();
        _registry.Register(1, live);

        _registry.Unregister(1, live);

        _registry.TryCancel(1).Should().BeFalse();
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
            .Select(i =>
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

    #region An implementation written before the owner-checked overload

    [Test]
    public void OwnerCheckedUnregister_OnAnImplementationWithoutIt_RemovesById()
    {
        ICancellationRegistry registry = new IdOnlyRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(7, cts);

        registry.Unregister(7, cts);

        registry
            .TryCancel(7)
            .Should()
            .BeFalse("the default overload falls back to the implementation's remove by id");
        cts.IsCancellationRequested.Should().BeFalse();
    }

    /// <summary>A host's registry that implements only the members that predate the overload.</summary>
    private sealed class IdOnlyRegistry : ICancellationRegistry
    {
        private readonly Dictionary<long, CancellationTokenSource> _sources = new();

        public void Register(long metadataId, CancellationTokenSource cts) =>
            _sources.TryAdd(metadataId, cts);

        public void Unregister(long metadataId) => _sources.Remove(metadataId);

        public bool TryCancel(long metadataId)
        {
            if (!_sources.TryGetValue(metadataId, out var cts))
                return false;
            cts.Cancel();
            return true;
        }
    }

    #endregion
}
