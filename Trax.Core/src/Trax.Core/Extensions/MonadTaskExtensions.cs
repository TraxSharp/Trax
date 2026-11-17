using System.Runtime.CompilerServices;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;

namespace Trax.Core.Train;

/// <summary>
/// Helpers to lift a <see cref="Task{Monad}"/> into a <see cref="MonadTask{TInput, TReturn}"/>
/// for fluent continuation, and to provide implicit-style conversions.
/// </summary>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal static class MonadTaskExtensions
{
    /// <summary>
    /// Converts a <see cref="Task{Monad}"/> into a <see cref="MonadTask{TInput, TReturn}"/>
    /// so the fluent chain can continue with single-type-arg method calls.
    /// </summary>
    public static MonadTask<TInput, TReturn> AsMonadTask<TInput, TReturn>(
        this Task<Monad<TInput, TReturn>> source
    ) => new(source);
}
