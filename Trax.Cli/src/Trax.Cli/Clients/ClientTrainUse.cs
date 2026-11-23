using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;
using Trax.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>
/// A train or query model a client document selects: its manifest entry (a query model's written as a train's, under
/// the query root), its field, the path to its value, and whether it is a query model.
/// </summary>
internal sealed record ClientTrainUse(
    ClientManifestTrain Train,
    GraphQLField Field,
    IReadOnlyList<string> Path,
    bool QueryModel = false
);
