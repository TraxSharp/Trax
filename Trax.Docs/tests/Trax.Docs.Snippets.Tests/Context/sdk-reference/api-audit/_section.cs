// What the examples under sdk-reference/api-audit take as given: the audit and GraphQL usings,
// and a sink to register. The service collection comes from _shared.cs.
global using System.Text.Json.Nodes;
global using Trax.Api.GraphQL.Audit;
global using Trax.Api.GraphQL.Extensions;

public sealed class AuditTableSink : ITraxAuditSink
{
    public Task WriteAsync(IReadOnlyList<TraxAuditEntry> batch, CancellationToken ct) =>
        Task.CompletedTask;
}
