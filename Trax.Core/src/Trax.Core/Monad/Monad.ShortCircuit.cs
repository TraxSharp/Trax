using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Junction;
using Trax.Core.Utils;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Executes a junction with short-circuit behavior, meaning that Left (exception) results
    /// are ignored and don't stop the chain.
    /// </summary>
    internal Monad<TInput, TReturn> ShortCircuitChain<TJunction, TIn, TOut>(
        TJunction junction,
        TIn previousJunction,
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
    /// Executes a junction with short-circuit behavior, potentially ending the chain early
    /// if the junction returns a value of type TReturn.
    /// </summary>
    public Monad<TInput, TReturn> ShortCircuit<TJunction>()
        where TJunction : class
    {
        // Create an instance of the junction
        var junctionInstance = this.InitializeJunction<TJunction, TInput, TReturn>();

        if (junctionInstance is null)
            return this;

        return ShortCircuit<TJunction>(junctionInstance);
    }

    /// <summary>
    /// Executes a junction with short-circuit behavior, potentially ending the chain early
    /// if the junction returns a value of type TReturn.
    /// </summary>
    public Monad<TInput, TReturn> ShortCircuit<TJunction>(TJunction junctionInstance)
        where TJunction : class
    {
        // Extract the input and output types from the junction
        var (tIn, tOut) = ReflectionHelpers.ExtractJunctionTypeArguments<TJunction>();

        // Find the appropriate ShortCircuitChain method to call
        var chainMethod = ReflectionHelpers.FindGenericChainInternalMethod<
            TJunction,
            TInput,
            TReturn
        >(this, tIn, tOut, 3);

        // Extract the input from Memory
        var input = MonadExtensions.ExtractTypeFromMemory(this, tIn);

        if (input is null)
        {
            Exception ??= new TrainException($"Could not find ({tIn}) in Memory.");
            return this;
        }

        // Execute the junction
        object[] parameters = [junctionInstance, input, null!];
        var result = chainMethod.Invoke(this, parameters);
        var outParam = parameters[2];

        // If the junction returns a value of type TReturn, set it as the ShortCircuitValue
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
