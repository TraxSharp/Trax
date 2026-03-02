using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Step;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    #region Chain<TStep, TIn, TOut>

    /// <summary>
    /// Executes a step with the provided input and captures the output.
    /// This is the core Chain method that all other Chain methods ultimately call.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn, TOut>(
        TStep step,
        Either<Exception, TIn> previousStep,
        out Either<Exception, TOut> outVar
    )
        where TStep : IStep<TIn, TOut>
    {
        // If there's already an exception, short-circuit
        if (Exception is not null)
        {
            outVar = Exception;
            return this;
        }

        // Execute the step directly without thread pool scheduling
        var task = step.RailwayStep(previousStep, Train);

        if (task.IsCompletedSuccessfully)
        {
            outVar = task.Result;
        }
        else
        {
            // Suppress the SynchronizationContext so that continuations from the step's
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
    /// Executes a step with input extracted from Memory.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn, TOut>(TStep step)
        where TStep : IStep<TIn, TOut>
    {
        // Extract the input from Memory
        var input = this.ExtractTypeFromMemory<TIn, TInput, TReturn>();

        if (input is null)
            return this;

        return Chain<TStep, TIn, TOut>(step, input, out var x);
    }

    /// <summary>
    /// Creates and executes a step with the provided input.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn, TOut>(
        Either<Exception, TIn> previousStep,
        out Either<Exception, TOut> outVar
    )
        where TStep : IStep<TIn, TOut>, new() =>
        Chain<TStep, TIn, TOut>(new TStep(), previousStep, out outVar);

    /// <summary>
    /// Creates and executes a step with input extracted from Memory.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn, TOut>()
        where TStep : IStep<TIn, TOut>, new() => Chain<TStep, TIn, TOut>(new TStep());

    #endregion

    #region Chain<TStep>

    /// <summary>
    /// Executes a step that is resolved from Memory by its interface type.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    internal Monad<TInput, TReturn> IChain<TStep>()
        where TStep : class
    {
        var stepType = typeof(TStep);

        // Verify that TStep is an interface
        if (!stepType.IsInterface)
        {
            Exception ??= new TrainException(
                $"Step ({stepType}) must be an interface to call IChain."
            );

            return this;
        }

        // Extract the step instance from Memory
        var stepService = this.ExtractTypeFromMemory<TStep, TInput, TReturn>();

        if (stepService is null)
            return this;

        return Chain<TStep>(stepService);
    }

    /// <summary>
    /// Creates and executes a step by its type.
    /// </summary>
    public Monad<TInput, TReturn> Chain<TStep>()
        where TStep : class
    {
        if (Exception is not null)
            return this;

        // Create an instance of the step
        var stepInstance = this.InitializeStep<TStep, TInput, TReturn>();

        if (stepInstance is null)
            return this;

        return Chain<TStep>(stepInstance);
    }

    /// <summary>
    /// Executes a step instance.
    /// </summary>
    public Monad<TInput, TReturn> Chain<TStep>(TStep stepInstance)
        where TStep : class
    {
        // Extract the input and output types from the step
        var (tIn, tOut) = ReflectionHelpers.ExtractStepTypeArguments<TStep>();

        // Find the appropriate Chain method to call
        var chainMethod = ReflectionHelpers.FindGenericChainMethod<TStep, TInput, TReturn>(
            this,
            tIn,
            tOut,
            1
        );

        // Execute the step
        var result = chainMethod.Invoke(this, [stepInstance]);

        return (Monad<TInput, TReturn>)result!;
    }

    #endregion

    #region Chain<TStep, TIn>

    /// <summary>
    /// Executes a step with the provided input and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn>(
        TStep step,
        Either<Exception, TIn> previousStep
    )
        where TStep : IStep<TIn, Unit> => Chain<TStep, TIn, Unit>(step, previousStep, out var x);

    /// <summary>
    /// Executes a step with input extracted from Memory and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn>(TStep step)
        where TStep : IStep<TIn, Unit> => Chain<TStep, TIn, Unit>(step);

    /// <summary>
    /// Creates and executes a step with the provided input and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn>(Either<Exception, TIn> previousStep)
        where TStep : IStep<TIn, Unit>, new() =>
        Chain<TStep, TIn, Unit>(new TStep(), previousStep, out var x);

    /// <summary>
    /// Creates and executes a step with input extracted from Memory and Unit output.
    /// </summary>
    internal Monad<TInput, TReturn> Chain<TStep, TIn>()
        where TStep : IStep<TIn, Unit>, new() => Chain<TStep, TIn, Unit>(new TStep());

    #endregion
}
