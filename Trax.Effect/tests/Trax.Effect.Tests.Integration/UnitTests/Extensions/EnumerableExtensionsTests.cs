using AwesomeAssertions;
using NUnit.Framework;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Integration.UnitTests.Extensions;

[TestFixture]
public class EnumerableExtensionsTests
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
}
