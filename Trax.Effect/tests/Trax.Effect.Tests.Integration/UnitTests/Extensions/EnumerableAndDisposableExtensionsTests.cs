using FluentAssertions;
using NUnit.Framework;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Extensions;

[TestFixture]
public class EnumerableAndDisposableExtensionsTests
{
    #region RunAll(action)

    [Test]
    public void RunAll_Action_RunsForEveryItem()
    {
        var seen = new List<int>();

        new[] { 1, 2, 3 }.RunAll(seen.Add);

        seen.Should().Equal(1, 2, 3);
    }

    [Test]
    public void RunAll_Action_OneThrows_ContinuesWithOthers()
    {
        var seen = new List<int>();

        new[] { 1, 2, 3 }.RunAll(i =>
        {
            if (i == 2)
                throw new InvalidOperationException();
            seen.Add(i);
        });

        seen.Should().Equal(1, 3);
    }

    #endregion

    #region RunAll(func)

    [Test]
    public void RunAll_Func_AppliesFunctionToEach()
    {
        var result = new[] { 1, 2, 3 }.RunAll(i => i * 2);

        result.Should().Equal(2, 4, 6);
    }

    #endregion

    #region RunAllAsync(func -> Task<T>)

    [Test]
    public async Task RunAllAsync_FuncTaskOfT_AwaitsEach()
    {
        var result = await new[] { 1, 2, 3 }.RunAllAsync(async i =>
        {
            await Task.Yield();
            return i + 1;
        });

        result.Should().Equal(2, 3, 4);
    }

    #endregion

    #region RunAllAsync(func -> Task)

    [Test]
    public async Task RunAllAsync_FuncTask_RunsSequentially()
    {
        var seen = new List<int>();

        await new[] { 1, 2, 3 }.RunAllAsync(async i =>
        {
            await Task.Yield();
            seen.Add(i);
        });

        seen.Should().Equal(1, 2, 3);
    }

    #endregion

    #region IsDisposed

    [Test]
    public void IsDisposed_FieldNotPresent_ReturnsFalse()
    {
        var d = new NoDisposeField();

        d.IsDisposed().Should().BeFalse();
    }

    [Test]
    public void IsDisposed_UnderscoreDisposedTrue_ReturnsTrue()
    {
        var d = new HasUnderscoreDisposed();
        d.Dispose();

        d.IsDisposed().Should().BeTrue();
    }

    [Test]
    public void IsDisposed_DisposedValueTrue_ReturnsTrue()
    {
        var d = new HasDisposedValue();
        d.Dispose();

        d.IsDisposed().Should().BeTrue();
    }

    [Test]
    public void IsDisposed_IsDisposedTrue_ReturnsTrue()
    {
        var d = new HasIsDisposed();
        d.Dispose();

        d.IsDisposed().Should().BeTrue();
    }

    [Test]
    public void IsDisposed_NotYetDisposed_ReturnsFalse()
    {
        var d = new HasUnderscoreDisposed();

        d.IsDisposed().Should().BeFalse();
    }

    [Test]
    public void IsDisposed_NullArgument_Throws()
    {
        IDisposable d = null!;

        Action act = () => d.IsDisposed();

        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    private class NoDisposeField : IDisposable
    {
        public void Dispose() { }
    }

    private class HasUnderscoreDisposed : IDisposable
    {
#pragma warning disable CS0414
        private bool _disposed = false;
#pragma warning restore CS0414

        public void Dispose() => _disposed = true;
    }

    private class HasDisposedValue : IDisposable
    {
#pragma warning disable CS0414
        private bool disposedValue = false;
#pragma warning restore CS0414

        public void Dispose() => disposedValue = true;
    }

    private class HasIsDisposed : IDisposable
    {
#pragma warning disable CS0414
        private bool isDisposed = false;
#pragma warning restore CS0414

        public void Dispose() => isDisposed = true;
    }
}
