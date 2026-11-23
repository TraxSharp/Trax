using System.Text;
using System.Text.Json;
using GraphQLParser;
using GraphQLParser.AST;

namespace Trax.Cli.Clients;

/// <summary>
/// What <c>trax client --operations</c> generates from: the operations schema's SDL and the documents the console
/// sends.
/// </summary>
/// <param name="Schema">The operations schema, as SDL (Trax.Api.Dashboard's <c>operations.graphql</c>).</param>
/// <param name="Documents">The <c>.graphql</c> files the console wrote.</param>
internal sealed record OperationsGenerateOptions(
    string Schema,
    IReadOnlyList<ClientDocumentFile> Documents
);
