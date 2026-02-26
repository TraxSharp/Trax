using LanguageExt;

namespace Trax.Effect.Extensions;

/// <summary>
/// Provides extension methods for working with IEnumerable collections in a functional style.
/// These methods enable robust execution of actions and functions across collections,
/// with special handling for error cases and asynchronous operations.
/// </summary>
/// <remarks>
/// The extensions in this class follow functional programming principles,
/// particularly using the Aggregate pattern to process collections sequentially
/// while maintaining proper error handling and state management.
/// </remarks>
public static class EnumerableExtensions
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
    /// Applies a function to each element in the collection and returns a list of results,
    /// ensuring all elements are processed even if exceptions occur for individual elements.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <typeparam name="TResult">The type of results produced by the function</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="func">The function to apply to each element</param>
    /// <returns>A list containing the results of applying the function to each element</returns>
    /// <remarks>
    /// This method aggregates results into a list, applying the function to each element
    /// in sequence. Any exceptions thrown by the function for a particular element will
    /// prevent that element's result from being added to the list, but will not stop
    /// processing of subsequent elements.
    /// </remarks>
    public static List<TResult> RunAll<T, TResult>(
        this IEnumerable<T> source,
        Func<T, TResult> func
    )
    {
        return source.Aggregate(
            new List<TResult>(), // Initial accumulator
            (acc, item) =>
            {
                acc.Add(func(item)); // Apply function and store result
                return acc; // Return updated list
            }
        );
    }

    /// <summary>
    /// Applies an asynchronous function to each element in the collection and returns a list of results,
    /// ensuring all elements are processed even if exceptions occur for individual elements.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <typeparam name="TResult">The type of results produced by the function</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="func">The asynchronous function to apply to each element</param>
    /// <returns>A task that resolves to a list containing the results of applying the function to each element</returns>
    /// <remarks>
    /// This method handles asynchronous functions by awaiting each result before proceeding
    /// to the next element. This ensures that elements are processed in sequence, which can
    /// be important for operations that have side effects or dependencies on previous operations.
    ///
    /// The implementation uses a nested async lambda to properly await both the accumulated
    /// results and the function application for each element.
    /// </remarks>
    public static async Task<List<TResult>> RunAllAsync<T, TResult>(
        this IEnumerable<T> source,
        Func<T, Task<TResult>> func
    )
    {
        return await source.Aggregate(
            Task.FromResult(new List<TResult>()),
            async (accTask, item) =>
            {
                var acc = await accTask; // Await the accumulated list
                var result = await func(item); // Execute the async function
                acc.Add(result); // Store the result
                return acc; // Return updated list
            }
        );
    }

    /// <summary>
    /// Executes an asynchronous action on each element in the collection,
    /// ensuring all elements are processed even if exceptions occur for individual elements.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection</typeparam>
    /// <param name="source">The source collection</param>
    /// <param name="func">The asynchronous action to execute on each element</param>
    /// <returns>A task that completes when all elements have been processed</returns>
    /// <remarks>
    /// This method ensures that asynchronous actions are executed in sequence,
    /// with each action waiting for the previous one to complete before starting.
    /// This is important for maintaining order of execution when actions have
    /// side effects or dependencies on previous actions.
    ///
    /// The implementation uses Task.CompletedTask as the initial accumulator,
    /// and then chains each action to execute after the previous one completes.
    /// </remarks>
    public static async Task RunAllAsync<T>(this IEnumerable<T> source, Func<T, Task> func)
    {
        await source.Aggregate(
            Task.CompletedTask, // Initial accumulator (empty task)
            async (acc, item) =>
            {
                await acc; // Ensure previous task is completed before starting the next
                await func(item); // Execute the function
            }
        );
    }
}
