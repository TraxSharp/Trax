namespace Trax.Core.Route;

/// <summary>
/// Defines the contract for a route — a unit of execution that takes an input and produces an output.
/// A route represents the two-track contract: given an input, produce an output or fail.
/// </summary>
/// <typeparam name="TIn">The type of input the route accepts</typeparam>
/// <typeparam name="TOut">The type of output the route produces</typeparam>
public interface IRoute<in TIn, TOut>
{
    /// <summary>
    /// Executes the route with the provided input.
    /// </summary>
    /// <param name="input">The input data for the route</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>The result produced by the route</returns>
    Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);
}
