using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Core.Train;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    #region Internal junction execution (short-circuit)

    /// <summary>
    /// Executes a junction with short-circuit behavior, meaning that Left (exception) results
    /// are ignored and don't stop the chain. Reflection-invoked from
    /// <see cref="ShortCircuit{TJunction}(TJunction)"/>.
    /// </summary>
    internal async Task<(
        Monad<TInput, TReturn> Monad,
        Either<Exception, TOut> Result
    )> ShortCircuitJunction<TJunction, TIn, TOut>(TJunction junction, TIn previousJunction)
        where TJunction : IJunction<TIn, TOut>
    {
        if (Exception is not null)
            return (this, (Exception)Exception);

        var result = await junction.RailwayJunction(previousJunction, Train).ConfigureAwait(false);

        // We skip the Left for Short Circuiting - only process Right results
        if (result.IsRight)
        {
            var outValue = result.Unwrap()!;

            if (typeof(TOut).IsTuple())
                this.AddTupleToMemory(outValue);
            else
                Memory[typeof(TOut)] = outValue;
        }

        return (this, result);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Executes a junction with short-circuit behavior, potentially ending the chain early
    /// if the junction returns a value of type TReturn.
    /// </summary>
    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>()
        where TJunction : class => new(ShortCircuitAsync<TJunction>());

    private Task<Monad<TInput, TReturn>> ShortCircuitAsync<TJunction>()
        where TJunction : class
    {
        var junctionInstance = this.InitializeJunction<TJunction, TInput, TReturn>();

        if (junctionInstance is null)
            return Task.FromResult(this);

        return ShortCircuitAsync(junctionInstance);
    }

    /// <summary>
    /// Executes a junction with short-circuit behavior, potentially ending the chain early
    /// if the junction returns a value of type TReturn.
    /// </summary>
    public MonadTask<TInput, TReturn> ShortCircuit<TJunction>(TJunction junctionInstance)
        where TJunction : class => new(ShortCircuitAsync(junctionInstance));

    private async Task<Monad<TInput, TReturn>> ShortCircuitAsync<TJunction>(
        TJunction junctionInstance
    )
        where TJunction : class
    {
        var (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TJunction>();

        var chainMethod = ReflectionHelpers.FindGenericShortCircuitJunctionMethod<
            TJunction,
            TInput,
            TReturn
        >(this, tIn, tOut, 2);

        var input = MonadExtensions.ExtractTypeFromMemory(this, tIn);

        if (input is null)
        {
            Exception ??= new TrainException($"Could not find ({tIn}) in Memory.");
            return this;
        }

        // Invoke the generic ShortCircuitJunction — returns Task<(Monad, Either<Exception, TOut>)>
        var taskObj = chainMethod.Invoke(this, [junctionInstance, input])!;

        // Await the dynamic Task<...>
        var task = (Task)taskObj;
        await task.ConfigureAwait(false);

        // Extract the Result property (the tuple) from Task<TResult>.
        // Named ValueTuple elements (Monad, Result) are stored as Item1, Item2 at runtime.
        var resultProperty = taskObj.GetType().GetProperty("Result")!;
        var tuple = resultProperty.GetValue(taskObj)!;
        var tupleItem2Field = tuple.GetType().GetField("Item2")!;
        var eitherResult = tupleItem2Field.GetValue(tuple)!;

        var maybeRightValue = ReflectionHelpers.GetRightFromDynamicEither(eitherResult);
        if (maybeRightValue.IsSome)
        {
            object rightValue = maybeRightValue.ValueUnsafe()!;
            FunctionalExtensions.AssertLoaded(rightValue);
            ShortCircuitValue = (TReturn)rightValue;
            ShortCircuitValueSet = true;
        }

        return this;
    }

    #endregion
}
