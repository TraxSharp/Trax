using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <see cref="Actor"/> has one kind, <see cref="Actor.Client"/>, and code outside Trax cannot add another: a record
/// deriving from it does not compile. Every rule check accepts only a client, so a kind of its own would fail closed
/// anyway, but the hierarchy is closed by the compiler, not by convention.
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class ActorHierarchyTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    [Test]
    public void A_record_outside_Trax_cannot_derive_from_Actor()
    {
        var errors = Compile(
            """
            using Trax.Effect.StateMachine.Persistence;

            public sealed record Other : Actor
            {
                public Other(Actor actor) : base(actor) { }
            }
            """
        );

        errors.Should().NotBeEmpty($"only Trax declares a kind of actor. See {Adr}");
    }

    [Test]
    public void A_client_is_built_outside_Trax()
    {
        // The probe compiles host code at all: the refusal above is the hierarchy's, not the setup's.
        var errors = Compile(
            """
            using Trax.Effect.StateMachine.Persistence;

            public static class Host
            {
                public static Actor Client() => new Actor.Client("user", "Operator");
            }
            """
        );

        errors.Should().BeEmpty();
    }

    [Test]
    public void Actor_offers_no_trusted_kind()
    {
        typeof(Actor)
            .GetNestedTypes()
            .Where(t => t.IsSubclassOf(typeof(Actor)))
            .Should()
            .BeEquivalentTo(
                [typeof(Actor.Client)],
                $"host code on a user's instance acts as a client holding no roles. See {Adr}"
            );
    }

    private static List<string> Compile(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(Actor).Assembly.Location)
            .Distinct()
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation
            .Create(
                "ActorProbe",
                [CSharpSyntaxTree.ParseText(source)],
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            )
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
    }
}
