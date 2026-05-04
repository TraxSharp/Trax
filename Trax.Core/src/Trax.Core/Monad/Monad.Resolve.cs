using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;

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
    /// This is typically the last method called in a train's Junctions implementation.
    /// </summary>
    /// <returns>Either the chain's result or an exception</returns>
    public Either<Exception, TReturn> Resolve()
    {
        if (Exception is not null)
            return Exception;

        if (ShortCircuitValueSet)
            return ShortCircuitValue;

        var result = this.ExtractTypeFromMemory<TReturn, TInput, TReturn>();

        if (result is null)
            return new TrainException($"Could not find type: ({typeof(TReturn)}).");

        return (TReturn)result;
    }
}
