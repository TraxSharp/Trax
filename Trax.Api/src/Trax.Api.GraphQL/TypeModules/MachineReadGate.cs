using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HotChocolate.Resolvers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Attributes;
using Trax.Effect.Utils;
using Trax.Mediator.Services.TrainExecution;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// Serialises the reads of one request: <c>get</c> reads through the request's data context, which serves one
/// operation at a time, and HotChocolate resolves two aliased reads at once.
/// </summary>
internal sealed class MachineReadGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Runs <paramref name="read"/> once no other read of this request is running.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await read().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
