using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using LanguageExt;

namespace Trax.Core.Monad;

public partial class Monad<TInput, TReturn>
{
    /// <summary>
    /// Resolves the chain with the provided return value.
    /// This method is used when you already have an Either result to return.
    /// </summary>
    /// <param name="returnType">The Either result to return, unless there's an exception</param>
    /// <returns>Either the provided result or the chain's exception</returns>
    public Either<Exception, TReturn> Resolve(Either<Exception, TReturn> returnType) =>
        Exception ?? returnType;

    /// <summary>
    /// Resolves the chain by extracting the result from Memory.
    /// This is typically the last method called in a train's RunInternal implementation.
    /// </summary>
    /// <returns>Either the chain's result or an exception</returns>
    public Either<Exception, TReturn> Resolve()
    {
        // If there's an exception, return it
        if (Exception is not null)
            return Exception;

        // If a short-circuit value is set, return it
        if (ShortCircuitValueSet)
            return ShortCircuitValue;

        // Try to extract the result type from Memory
        var result = this.ExtractTypeFromMemory<TReturn, TInput, TReturn>();

        // If the result type can't be found, return an exception
        if (result is null)
            return new WorkflowException($"Could not find type: ({typeof(TReturn)}).");

        return (TReturn)result;
    }
}
