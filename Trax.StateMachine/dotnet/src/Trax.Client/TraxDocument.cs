namespace Trax.Client;

/// <summary>
/// One document a client sends: its operation's name and kind, the id a host registers it under, and its text exactly
/// as the id was computed from it.
/// </summary>
/// <param name="Name">The operation's name, sent as <c>operationName</c>.</param>
/// <param name="Kind">Query, mutation or subscription.</param>
/// <param name="Id">The persisted id, or null for a document a host runs as text and never registers.</param>
/// <param name="Text">The document's text.</param>
public abstract record TraxDocument(string Name, TraxOperationKind Kind, string? Id, string Text);

/// <summary>A document whose variables are a <typeparamref name="TVariables"/> and whose data is a <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TVariables">The variables record; <see cref="TraxNoVariables"/> for a document that takes none.</typeparam>
/// <typeparam name="TResult">The record the answer's <c>data</c> is read into.</typeparam>
public sealed record TraxDocument<TVariables, TResult>(
    string Name,
    TraxOperationKind Kind,
    string? Id,
    string Text
) : TraxDocument(Name, Kind, Id, Text);
