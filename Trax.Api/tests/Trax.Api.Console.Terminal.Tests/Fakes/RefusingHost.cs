using System.Text.Json.Nodes;
using Trax.Api.Console.Terminal.Connection;
using Trax.Client;

namespace Trax.Api.Console.Terminal.Tests.Fakes;

/// <summary>A host that refuses the operations named, with the message and code given, and answers the rest as
/// <paramref name="inner"/> does.</summary>
internal sealed class RefusingHost(ITraxTransport inner) : ITraxTransport
{
    private readonly Dictionary<string, TraxGraphQLError> _refusals = new(StringComparer.Ordinal);

    public RefusingHost Refuse(string operation, string message, string? code = null)
    {
        lock (_refusals)
            _refusals[operation] = new TraxGraphQLError(
                message,
                code,
                Path: null,
                Extensions: null
            );
        return this;
    }

    public Task<TraxAnswer> ExecuteAsync(
        TraxRequest request,
        CancellationToken cancellationToken = default
    )
    {
        TraxGraphQLError? refusal;
        lock (_refusals)
            _refusals.TryGetValue(request.Document.Name, out refusal);
        return refusal is null
            ? inner.ExecuteAsync(request, cancellationToken)
            : Task.FromResult(new TraxAnswer { Errors = [refusal] });
    }

    public IDisposable Subscribe(TraxRequest request, ISubscriptionSink sink) =>
        inner.Subscribe(request, sink);
}
