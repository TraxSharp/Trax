namespace Trax.Effect.Data.Enums;

/// <summary>
/// Defines the strategies for evaluating database queries in the Trax.Effect.Data system.
/// </summary>
/// <remarks>
/// The EvaluationStrategy enum represents different approaches to query execution timing.
/// It allows the system to control when and how database queries are executed,
/// which can impact performance and behavior.
///
/// This enum is particularly useful for:
/// 1. Controlling query execution behavior in different scenarios
/// 2. Optimizing performance for different types of operations
/// 3. Providing flexibility in how data is loaded and processed
///
/// The choice of evaluation strategy can significantly impact application performance,
/// especially for complex queries or large datasets.
/// </remarks>
public enum EvaluationStrategy
{
    /// <summary>
    /// Executes queries immediately when they are defined.
    /// </summary>
    /// <remarks>
    /// The Eager strategy causes queries to be executed as soon as they are defined,
    /// rather than when their results are accessed. This can be beneficial when:
    ///
    /// 1. You need to ensure data is loaded upfront
    /// 2. You want to detect database errors early
    /// 3. You're working with time-sensitive data that shouldn't change during processing
    ///
    /// However, eager evaluation may lead to unnecessary database queries if the
    /// results are not actually used later in the code.
    /// </remarks>
    Eager,

    /// <summary>
    /// Defers query execution until the results are actually accessed.
    /// </summary>
    /// <remarks>
    /// The Lazy strategy delays query execution until the results are actually needed.
    /// This can be beneficial when:
    ///
    /// 1. You want to avoid unnecessary database queries
    /// 2. You're building complex queries that may be conditionally executed
    /// 3. You need to optimize performance by only loading data when it's needed
    ///
    /// Lazy evaluation is the default behavior in Entity Framework Core and is generally
    /// more efficient for most scenarios.
    /// </remarks>
    Lazy
}
