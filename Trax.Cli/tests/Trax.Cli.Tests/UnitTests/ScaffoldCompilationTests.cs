using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Generator;
using Trax.Cli.Models;
using OperationKind = Trax.Cli.Models.OperationKind;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// A scaffolded project has to compile against the Trax packages it references. The generated
/// csproj floats on <c>1.*</c>, so a train template written against an API Trax later removes
/// breaks every new scaffold the moment that release ships, and string checks on the template do
/// not notice. This compiles what the generator renders against the Trax assemblies the tests run
/// with.
///
/// <para>Those are not the Trax a new scaffold floats to. The tests reference no Trax.Core
/// directly: it arrives transitively through the <c>Trax.Effect.StateMachine.Persistence</c> pin
/// in <c>Directory.Packages.props</c>, which currently resolves Trax.Core 1.6.0, and 1.6.0 still
/// has <c>RunInternal</c> and <c>Activate</c>. So in CI a template that goes back to the old shape
/// still compiles here, and this only catches that regression once the pin moves to a Trax.Core
/// without them (or locally, where <c>trax-local.props</c> swaps in the freshly packed build).
/// Until then <c>ScaffoldTrainShapeTests</c> is what guards the train shape.</para>
/// </summary>
public class ScaffoldCompilationTests
{
    [TestCase(OperationKind.Query)]
    [TestCase(OperationKind.Mutation)]
    public void A_rendered_train_compiles_against_the_referenced_Trax(OperationKind kind)
    {
        var renderer = new CodeRenderer();
        var op = new ApiOperation
        {
            Name = "GetPlayer",
            Kind = kind,
            Group = "Players",
            InputType = new ApiType
            {
                Name = "GetPlayerInput",
                Fields =
                [
                    new ApiField
                    {
                        Name = "Id",
                        TypeName = "Guid",
                        IsRequired = true,
                    },
                ],
            },
            OutputType = new ApiType
            {
                Name = "GetPlayerOutput",
                Fields =
                [
                    new ApiField
                    {
                        Name = "Result",
                        TypeName = "string",
                        IsRequired = true,
                    },
                ],
            },
        };

        AssertCompiles(
            renderer,
            op,
            [
                ("GraphQLNamespaces.cs", renderer.RenderGraphQLNamespaces(["Players"], "MyApi")),
                ("Input.cs", renderer.RenderInput(op, "MyApi")),
                ("Output.cs", renderer.RenderOutput(op, "MyApi")),
            ]
        );
    }

    /// <summary>
    /// An operation that returns nothing scaffolds a train whose output is LanguageExt's
    /// <c>Unit</c>, which the train, interface and junction templates import only on that
    /// branch. No output record is written for it.
    /// </summary>
    [Test]
    public void A_rendered_train_with_a_Unit_output_compiles_against_the_referenced_Trax()
    {
        var renderer = new CodeRenderer();
        var op = new ApiOperation
        {
            Name = "DeletePlayer",
            Kind = OperationKind.Mutation,
            Group = "Players",
            InputType = new ApiType
            {
                Name = "DeletePlayerInput",
                Fields =
                [
                    new ApiField
                    {
                        Name = "Id",
                        TypeName = "Guid",
                        IsRequired = true,
                    },
                ],
            },
            OutputType = new ApiType { Name = "Unit", IsBuiltIn = true },
        };

        renderer
            .RenderJunction(op, "MyApi")
            .Should()
            .Contain("Junction<DeletePlayerInput, Unit>", "the premise is the Unit branch");

        AssertCompiles(
            renderer,
            op,
            [
                ("GraphQLNamespaces.cs", renderer.RenderGraphQLNamespaces(["Players"], "MyApi")),
                ("Input.cs", renderer.RenderInput(op, "MyApi")),
            ]
        );
    }

    private static void AssertCompiles(
        CodeRenderer renderer,
        ApiOperation op,
        (string Name, string Source)[] models
    )
    {
        (string Name, string Source)[] sources =
        [
            .. models,
            (
                "GlobalUsings.cs",
                """
                global using System;
                global using System.Collections.Generic;
                global using System.Linq;
                global using System.Threading.Tasks;
                """
            ),
            ("ITrain.cs", renderer.RenderTrainInterface(op, "MyApi")),
            ("Train.cs", renderer.RenderTrainImplementation(op, "MyApi")),
            ("Junction.cs", renderer.RenderJunction(op, "MyApi")),
        ];

        var errors = string.Join(
            Environment.NewLine,
            Compile(sources)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString())
        );

        errors
            .Should()
            .BeEmpty(
                "a new scaffold must build against the Trax it references, or the first thing a "
                    + "user sees is a compile error in code they did not write"
            );
    }

    private static IEnumerable<Diagnostic> Compile(
        IEnumerable<(string Name, string Source)> sources
    )
    {
        // Every assembly deployed with the tests, which includes the Trax packages and their
        // dependencies whether or not anything has loaded them yet.
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation
            .Create(
                "Scaffold",
                sources.Select(s => CSharpSyntaxTree.ParseText(s.Source, path: s.Name)),
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable
                )
            )
            .GetDiagnostics();
    }
}
