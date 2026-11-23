using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using GraphQLParser.Visitors;
using Trax.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>One <c>.graphql</c> file a frontend wrote, by the path it is named under and its text.</summary>
internal sealed record ClientDocumentFile(string Source, string Text);
