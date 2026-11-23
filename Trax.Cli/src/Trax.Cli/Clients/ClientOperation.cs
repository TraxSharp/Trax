using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;
using Trax.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>One operation a frontend wrote, checked, with the text and id its host registers it under.</summary>
internal sealed record ClientOperation(
    string Name,
    string Source,
    GraphQLOperationDefinition Operation,
    string Root,
    IReadOnlyList<ClientTrainUse> Trains,
    string Text,
    string Id
);
