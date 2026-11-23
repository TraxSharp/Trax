using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GraphQLParser;
using GraphQLParser.AST;
using Trax.Cli.Machines;
using Trax.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>One file of a generated client: its path under the output directory, and its text.</summary>
internal sealed record ClientFile(string Path, string Text);
