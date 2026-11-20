using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GraphQLParser;
using GraphQLParser.AST;
using Trax.Cli.Machines;
using Trax.StateMachine;

namespace Trax.Cli.Clients;

/// <summary>What <c>trax client</c> generates from, and how.</summary>
/// <param name="Manifest">The host's client manifest.</param>
/// <param name="Specifier">The module the generated code imports the runtime from.</param>
/// <param name="ToolsDir">The engine's <c>tools/</c>, when the bundled twin generator is not used.</param>
/// <param name="Documents">The <c>.graphql</c> files the frontend wrote, when <c>--documents</c> names any.</param>
/// <param name="DocumentGlobs">The globs that found them, relative to the output directory, for the editor's config.</param>
internal sealed record ClientGenerateOptions(
    ClientManifest Manifest,
    string Specifier = ClientGenerator.DefaultSpecifier,
    string? ToolsDir = null,
    IReadOnlyList<ClientDocumentFile>? Documents = null,
    IReadOnlyList<string>? DocumentGlobs = null
);
