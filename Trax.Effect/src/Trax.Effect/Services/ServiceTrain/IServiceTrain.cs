using Trax.Effect.Models.Metadata;
using Trax.Core.Route;

namespace Trax.Effect.Services.ServiceTrain;

/// <summary>
/// Defines the contract for trains that include database tracking and logging capabilities.
/// This interface extends the base IRoute interface to add metadata tracking.
/// </summary>
/// <typeparam name="TIn">The input type for the train</typeparam>
/// <typeparam name="TOut">The output type for the train</typeparam>
public interface IServiceTrain<in TIn, TOut> : IRoute<TIn, TOut>, IDisposable
{
    /// <summary>
    /// Executes the train with the given input and records execution details in the database.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>The result of the train execution</returns>
    new Task<TOut> Run(TIn input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the metadata associated with this train execution.
    /// Contains tracking information such as state, timing, and error details.
    /// </summary>
    public Metadata? Metadata { get; }
}
