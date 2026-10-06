using System;
using System.ComponentModel;
using System.Reflection;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Mediator.Configuration;

namespace Trax.Mediator.Extensions;

/// <summary>
/// Overloads that exist only to turn a builder call made in the wrong order into a compile error
/// that says which call comes first.
/// </summary>
/// <remarks>
/// Each builder stage is its own type, so a call made on the wrong stage does not compile. Without
/// these the compiler reports CS1929, naming the builder state types and leaving the reader to work
/// out the order from them. Each overload here takes the stage the call was wrongly made on, is
/// marked obsolete as an error carrying the instruction, and is hidden from completion. None of them
/// can run: a call that binds to one does not compile. The correct order binds to the real methods,
/// whose receiver types these never share.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BuilderOrderExtensions
{
    internal const string AddEffectsFirst = "Call AddEffects(...) before AddMediator(...).";

    internal const string AddMediatorOnce =
        "AddMediator(...) is already called. Call it once and configure everything in that call.";

    internal const string AddStateMachinesFirst =
        "Call AddStateMachines(...) before AddMediator(...).";

    /// <summary>Not callable: <c>AddMediator</c> comes after <c>AddEffects</c>.</summary>
    [Obsolete(AddEffectsFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilder builder,
        Func<TraxMediatorBuilder, TraxMediatorBuilder> configure
    ) => throw new InvalidOperationException(AddEffectsFirst);

    /// <summary>Not callable: <c>AddMediator</c> comes after <c>AddEffects</c>.</summary>
    [Obsolete(AddEffectsFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilder builder,
        params Assembly[] assemblies
    ) => throw new InvalidOperationException(AddEffectsFirst);

    /// <summary>Not callable: <c>AddMediator</c> is called once.</summary>
    [Obsolete(AddMediatorOnce, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilderWithMediator builder,
        Func<TraxMediatorBuilder, TraxMediatorBuilder> configure
    ) => throw new InvalidOperationException(AddMediatorOnce);

    /// <summary>Not callable: <c>AddMediator</c> is called once.</summary>
    [Obsolete(AddMediatorOnce, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilderWithMediator builder,
        params Assembly[] assemblies
    ) => throw new InvalidOperationException(AddMediatorOnce);

    /// <summary>
    /// Not callable: <c>AddStateMachines</c> (Trax.Effect.StateMachine.Persistence) comes before
    /// <c>AddMediator</c>, which reads the assemblies it contributes.
    /// </summary>
    [Obsolete(AddStateMachinesFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddStateMachines(
        this TraxBuilderWithMediator builder,
        params Assembly[] assemblies
    ) => throw new InvalidOperationException(AddStateMachinesFirst);

    /// <summary>
    /// Not callable: <c>AddStateMachines</c> (Trax.Effect.StateMachine.Persistence) comes before
    /// <c>AddMediator</c>, which reads the assemblies it contributes.
    /// </summary>
    /// <remarks>
    /// The options parameter is <c>dynamic</c> because this package does not reference the one that
    /// declares the options type, and a lambda written for the real overload must still bind here
    /// so that the error reported is this one.
    /// </remarks>
    [Obsolete(AddStateMachinesFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxBuilderWithMediator AddStateMachines(
        this TraxBuilderWithMediator builder,
        Action<dynamic> configure,
        params Assembly[] assemblies
    ) => throw new InvalidOperationException(AddStateMachinesFirst);
}
