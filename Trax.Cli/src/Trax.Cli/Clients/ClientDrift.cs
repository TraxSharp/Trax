using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GraphQLParser;
using GraphQLParser.AST;
using Trax.Cli.Machines;
using Trax.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>A file that differs between a generated client and the one on disk.</summary>
internal sealed record ClientDrift(string Path, DriftStatus Status);
