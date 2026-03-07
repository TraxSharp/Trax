using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Step;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Executes a step with short-circuit behavior, meaning that Left (exception) results
    /// are ignored and don't stop the chain.
    /// </summary>
    internal Monad<TInput, TReturn> ShortCircuitChain<TStep, TIn, TOut>(
        TStep step,
        TIn previousStep,
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
        outVar = task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult();

        // We skip the Left for Short Circuiting - only process Right results
        if (outVar.IsRight)
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
    /// Executes a step with short-circuit behavior, potentially ending the chain early
    /// if the step returns a value of type TReturn.
    /// </summary>
    public Monad<TInput, TReturn> ShortCircuit<TStep>()
        where TStep : class
    {
        // Create an instance of the step
        var stepInstance = this.InitializeStep<TStep, TInput, TReturn>();

        if (stepInstance is null)
            return this;

        return ShortCircuit<TStep>(stepInstance);
    }

    /// <summary>
    /// Executes a step with short-circuit behavior, potentially ending the chain early
    /// if the step returns a value of type TReturn.
    /// </summary>
    public Monad<TInput, TReturn> ShortCircuit<TStep>(TStep stepInstance)
        where TStep : class
    {
        // Extract the input and output types from the step
        var (tIn, tOut) = ReflectionHelpers.ExtractStepTypeArguments<TStep>();

        // Find the appropriate ShortCircuitChain method to call
        var chainMethod = ReflectionHelpers.FindGenericChainInternalMethod<TStep, TInput, TReturn>(
            this,
            tIn,
            tOut,
            3
        );

        // Extract the input from Memory
        var input = MonadExtensions.ExtractTypeFromMemory(this, tIn);

        if (input is null)
        {
            Exception ??= new TrainException($"Could not find ({tIn}) in Memory.");
            return this;
        }

        // Execute the step
        object[] parameters = [stepInstance, input, null!];
        var result = chainMethod.Invoke(this, parameters);
        var outParam = parameters[2];

        // If the step returns a value of type TReturn, set it as the ShortCircuitValue
        var maybeRightValue = ReflectionHelpers.GetRightFromDynamicEither(outParam);
        maybeRightValue.Iter(rightValue =>
        {
            FunctionalExtensions.AssertLoaded(rightValue);
            ShortCircuitValue = (TReturn)rightValue;
            ShortCircuitValueSet = true;
        });

        FunctionalExtensions.AssertLoaded(result);
        return (Monad<TInput, TReturn>)result;
    }
}
