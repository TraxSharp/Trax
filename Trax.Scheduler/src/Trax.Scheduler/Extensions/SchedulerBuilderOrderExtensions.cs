using System.ComponentModel;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Mediator.Configuration;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Extensions;

/// <summary>
/// Overloads that exist only to turn an <c>AddScheduler</c> call made before <c>AddMediator</c>
/// into a compile error that says which call comes first.
/// </summary>
/// <remarks>
/// <c>AddScheduler</c> extends the builder <c>AddMediator</c> returns, so on an earlier stage it
/// does not compile. Without these the compiler reports CS1929, naming the builder state types and
/// leaving the reader to work out the order from them. Each overload here takes a stage before the
/// mediator, is marked obsolete as an error carrying the instruction, and is hidden from
/// completion. None of them can run: a call that binds to one does not compile. The correct order
/// binds to the real methods in <see cref="SchedulerExtensions"/>, whose receiver type these never
/// share. Trax.Mediator's <c>BuilderOrderExtensions</c> does the same for its own calls.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SchedulerBuilderOrderExtensions
{
    internal const string AddMediatorFirst = "Call AddMediator(...) before AddScheduler(...).";

    /// <summary>Not callable: <c>AddScheduler</c> comes after <c>AddMediator</c>.</summary>
    [Obsolete(AddMediatorFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddScheduler(
        this TraxBuilderWithEffects builder,
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
    ) => throw new InvalidOperationException(AddMediatorFirst);

    /// <summary>Not callable: <c>AddScheduler</c> comes after <c>AddMediator</c>.</summary>
    [Obsolete(AddMediatorFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddScheduler(this TraxBuilderWithEffects builder) =>
        throw new InvalidOperationException(AddMediatorFirst);

    /// <summary>Not callable: <c>AddScheduler</c> comes after <c>AddMediator</c>.</summary>
    [Obsolete(AddMediatorFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddScheduler(
        this TraxBuilder builder,
        Func<SchedulerConfigurationBuilder, SchedulerConfigurationBuilder> configure
    ) => throw new InvalidOperationException(AddMediatorFirst);

    /// <summary>Not callable: <c>AddScheduler</c> comes after <c>AddMediator</c>.</summary>
    [Obsolete(AddMediatorFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddScheduler(this TraxBuilder builder) =>
        throw new InvalidOperationException(AddMediatorFirst);
}
