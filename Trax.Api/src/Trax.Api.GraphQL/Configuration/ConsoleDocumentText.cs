namespace Trax.Api.GraphQL.Configuration;

/// <summary>One document the operations console sends: its operation's name and its text.</summary>
/// <param name="Name">The operation's name, unique among the console's documents.</param>
/// <param name="Text">The text the console's GraphQL client sends for it.</param>
internal sealed record ConsoleDocumentText(string Name, string Text);
