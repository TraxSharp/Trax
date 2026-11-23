using System.Text;
using GraphQLParser;
using GraphQLParser.AST;
using Trax.Cli.Generator;

namespace Trax.Cli.Clients;

/// <summary>The language <c>trax client --operations</c> writes its client in.</summary>
internal enum OperationsLanguage
{
    /// <summary>The TypeScript client the React console sends through.</summary>
    TypeScript,

    /// <summary>A C# client over Trax.Client, for a .NET operator frontend.</summary>
    CSharp,
}
