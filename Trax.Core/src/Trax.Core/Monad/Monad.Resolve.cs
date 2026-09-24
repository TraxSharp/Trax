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
    /// <remarks>
    /// Not for a train's <c>Junctions()</c>: a declaration names the junction that produces the
    /// result rather than stating it, so reading a chain that ends this way records a refusal
    /// and the host will not start.
    /// </remarks>
    public Either<Exception, TReturn> Resolve(Either<Exception, TReturn> returnType)
    {
        if (Recorder is not null)
        {
            Recorder.Refuse(
                "it ends with Resolve(value), which states the result instead of declaring the "
                    + "junction that produces it. End the chain with Resolve() and chain a "
                    + $"junction that produces '{typeof(TReturn).FullName ?? typeof(TReturn).Name}'."
            );

            return new ChainRecordedException();
        }

        return Exception ?? returnType;
    }

    /// <summary>
    /// Resolves the chain by extracting the result from Memory.
    /// This is typically the last method called in a train's Junctions implementation.
    /// </summary>
    /// <returns>Either the chain's result or an exception</returns>
    public Either<Exception, TReturn> Resolve()
    {
        if (Recorder is not null)
        {
            Recorder.Record(ChainStepKind.Resolve, null, null, typeof(TReturn));

            // A recorded chain produces no value. The Left is a sentinel the reader discards;
            // returning a default Right would hand LanguageExt a null for a reference TReturn.
            return new ChainRecordedException();
        }

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
