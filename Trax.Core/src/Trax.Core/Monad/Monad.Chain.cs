using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    #region Chain<TJunction, TIn, TOut>

    /// <summary>
    /// Executes a junction with the provided input and captures the output.
    /// This is the core Chain method that all other Chain methods ultimately call.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn, TOut>(
        TJunction junction,
        Either<Exception, TIn> previousJunction,
        out Either<Exception, TOut> outVar
    )
        where TJunction : IJunction<TIn, TOut>
    {
        // If there's already an exception, short-circuit
        if (Exception is not null)
        {
            outVar = Exception;
            return this;
        }

        // Execute the junction directly without thread pool scheduling
        var task = junction.RailwayJunction(previousJunction, Train);

        if (task.IsCompletedSuccessfully)
        {
            outVar = task.Result;
        }
        else
        {
            // Suppress the SynchronizationContext so that continuations from the junction's
            // async work are posted to the thread pool instead of trying to marshal back
            // to the captured context. Without this, .GetAwaiter().GetResult() deadlocks
            // in environments with a single-threaded SynchronizationContext (Blazor Server,
            // WPF, legacy ASP.NET) because the blocked thread is the same one the
            // continuation needs to resume on.
            var savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                outVar = task.GetAwaiter().GetResult();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(savedContext);
            }
        }

        // Handle the result
        if (outVar.IsLeft)
            Exception ??= outVar.Swap().ValueUnsafe();
        else
        {
            var outValue = outVar.Unwrap()!;

            // Store the result in Memory
            if (typeof(TOut).IsTuple())
                this.AddTupleToMemory(outValue);
            else
                Memory[typeof(TOut)] = outValue;
        }

        return this;
    }

    /// <summary>
    /// Executes a junction with input extracted from Memory.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn, TOut>(TJunction junction)
        where TJunction : IJunction<TIn, TOut>
    {
        // Extract the input from Memory
        var input = this.ExtractTypeFromMemory<TIn, TInput, TReturn>();

        if (input is null)
            return this;

        return Chain<TJunction, TIn, TOut>(junction, input, out var x);
    }

    /// <summary>
    /// Creates and executes a junction with the provided input.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn, TOut>(
        Either<Exception, TIn> previousJunction,
        out Either<Exception, TOut> outVar
    )
        where TJunction : IJunction<TIn, TOut>, new() =>
        Chain<TJunction, TIn, TOut>(new TJunction(), previousJunction, out outVar);

    /// <summary>
    /// Creates and executes a junction with input extracted from Memory.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn, TOut>()
        where TJunction : IJunction<TIn, TOut>, new() =>
        Chain<TJunction, TIn, TOut>(new TJunction());

    #endregion

    #region Chain<TJunction>

    /// <summary>
    /// Executes a junction that is resolved from Memory by its interface type.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    internal Monad<TInput, TReturn> IChain<TJunction>()
        where TJunction : class
    {
        var junctionType = typeof(TJunction);

        // Verify that TJunction is an interface
        if (!junctionType.IsInterface)
        {
            Exception ??= new TrainException(
                $"Junction ({junctionType}) must be an interface to call IChain."
            );

            return this;
        }

        // Extract the junction instance from Memory
        var junctionService = this.ExtractTypeFromMemory<TJunction, TInput, TReturn>();

        if (junctionService is null)
            return this;

        return Chain<TJunction>(junctionService);
    }

    /// <summary>
    /// Creates and executes a junction by its type.
    /// </summary>
    public Monad<TInput, TReturn> Chain<TJunction>()
        where TJunction : class
    {
        if (Exception is not null)
            return this;

        // Create an instance of the junction
        var junctionInstance = this.InitializeJunction<TJunction, TInput, TReturn>();

        if (junctionInstance is null)
            return this;

        return Chain<TJunction>(junctionInstance);
    }

    /// <summary>
    /// Executes a junction instance.
    /// </summary>
    public Monad<TInput, TReturn> Chain<TJunction>(TJunction junctionInstance)
        where TJunction : class
    {
        // Extract the input and output types from the junction
        var (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TJunction>();

        // Find the appropriate Chain method to call
        var chainMethod = ReflectionHelpers.FindGenericChainMethod<TJunction, TInput, TReturn>(
            this,
            tIn,
            tOut,
            1
        );

        // Execute the junction
        var result = chainMethod.Invoke(this, [junctionInstance]);

        return (Monad<TInput, TReturn>)result!;
    }

    #endregion

    #region Chain<TJunction, TIn>

    /// <summary>
    /// Executes a junction with the provided input and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn>(
        TJunction junction,
        Either<Exception, TIn> previousJunction
    )
        where TJunction : IJunction<TIn, Unit> =>
        Chain<TJunction, TIn, Unit>(junction, previousJunction, out var x);

    /// <summary>
    /// Executes a junction with input extracted from Memory and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn>(TJunction junction)
        where TJunction : IJunction<TIn, Unit> => Chain<TJunction, TIn, Unit>(junction);

    /// <summary>
    /// Creates and executes a junction with the provided input and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn>(Either<Exception, TIn> previousJunction)
        where TJunction : IJunction<TIn, Unit>, new() =>
        Chain<TJunction, TIn, Unit>(new TJunction(), previousJunction, out var x);

    /// <summary>
    /// Creates and executes a junction with input extracted from Memory and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TJunction, TIn>()
        where TJunction : IJunction<TIn, Unit>, new() =>
        Chain<TJunction, TIn, Unit>(new TJunction());

    #endregion
}
