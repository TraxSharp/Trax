using System.Runtime.ExceptionServices;

namespace Trax.Effect.Extensions;

/// <summary>
/// Runs an action or function over each element of a collection, one element at a time in order.
/// Each method states what it does when an element throws, because they differ.
/// </summary>
internal static class EnumerableExtensions
{
    /// <summary>
    /// Executes an action on each element in the collection, ensuring all elements are processed
    /// even if exceptions occur for individual elements.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="action">The action to execute on each element</param>
    /// <remarks>
    /// This method uses a simple foreach approach to avoid creating closure chains that could
    /// hold references to objects longer than expected. Each action is executed immediately
    /// and any exceptions are caught and ignored to ensure all elements are processed.
    ///
    /// This implementation is more memory-efficient than the previous aggregate approach
    /// as it doesn't create a chain of lambda closures.
    /// </remarks>
    public static void RunAll<T>(this IEnumerable<T> source, Action<T> action)
    {
        foreach (var item in source)
        {
            try
            {
                action(item);
            }
            catch
            {
                // Ignore exceptions to ensure all elements are processed
                // This maintains the original behavior of RunAll where exceptions
                // in individual elements don't stop processing of subsequent elements
            }
        }
    }

    /// <summary>
    /// Applies a function to each element in the collection, in order, and returns the results.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <typeparam name="TResult">The type of results produced by the function</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="func">The function to apply to each element</param>
    /// <returns>A list containing the results of applying the function to each element</returns>
    /// <remarks>
    /// An exception from <paramref name="func"/> propagates and stops the remaining elements. The
    /// effect runners build their providers with it, so a factory that cannot create its provider
    /// fails the runner rather than letting a run proceed without that provider.
    /// </remarks>
    public static List<TResult> RunAll<T, TResult>(
        this IEnumerable<T> source,
        Func<T, TResult> func
    )
    {
        var results = new List<TResult>();

        foreach (var item in source)
            results.Add(func(item));

        return results;
    }

    /// <summary>
    /// Applies an asynchronous function to each element in the collection, one at a time in order,
    /// and returns the results.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <typeparam name="TResult">The type of results produced by the function</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="func">The asynchronous function to apply to each element</param>
    /// <returns>A task that resolves to a list containing the results of applying the function to each element</returns>
    /// <remarks>
    /// An exception from <paramref name="func"/> propagates and stops the remaining elements.
    /// </remarks>
    public static async Task<List<TResult>> RunAllAsync<T, TResult>(
        this IEnumerable<T> source,
        Func<T, Task<TResult>> func
    )
    {
        var results = new List<TResult>();

        foreach (var item in source)
            results.Add(await func(item));

        return results;
    }

    /// <summary>
    /// Runs an asynchronous action on every element in the collection, one at a time in order,
    /// even when an earlier one throws, and then rethrows what was thrown.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="func">The asynchronous action to execute on each element</param>
    /// <returns>A task that completes when every element has been processed</returns>
    /// <remarks>
    /// The effect runner fans each write out through this, so one provider's failure does not
    /// stop the next provider from recording the run: the data provider still writes the
    /// terminal state when an effect registered before it throws.
    ///
    /// When exactly one element threw, that exception is rethrown as it was, with its stack
    /// trace. When several threw and all of them were cancellations, the first cancellation is
    /// rethrown, so a cancelled save still surfaces as <see cref="OperationCanceledException"/>.
    /// Otherwise an <see cref="AggregateException"/> carries every exception, in element order.
    /// </remarks>
    public static async Task RunAllAsync<T>(this IEnumerable<T> source, Func<T, Task> func)
    {
        List<Exception>? failures = null;

        foreach (var item in source)
        {
            try
            {
                await func(item);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is null)
            return;

        if (failures.Count == 1 || failures.All(f => f is OperationCanceledException))
            ExceptionDispatchInfo.Capture(failures[0]).Throw();

        throw new AggregateException(failures);
    }
}
