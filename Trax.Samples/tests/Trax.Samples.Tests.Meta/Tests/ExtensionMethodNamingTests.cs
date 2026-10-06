using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Public extension methods in Extensions/ folders carry Trax in the name.
///
/// <para>Not ADR-enforcing: it pins a naming convention that was adopted rather than chosen, and nothing about it was a trade-off between real options.</para>
/// </summary>
[TestFixture]
public class ExtensionMethodNamingTests
{
    /// <summary>
    /// The roots holding this repo's own code. Unlike the seven repos that ship packages from a
    /// src/ folder, Trax.Samples has none: the samples, the shared test library and the shipped
    /// templates are the code.
    /// </summary>
    private static readonly string[] SourceRoots = ["samples", "lib", "templates"];

    private static readonly HashSet<string> HostingTypes = new(StringComparer.Ordinal)
    {
        "IServiceCollection",
        "IApplicationBuilder",
        "IEndpointRouteBuilder",
        "WebApplication",
        "WebApplicationBuilder",
    };

    /// <summary>
    /// Method names exempt from the Trax-naming convention. Each entry must justify why.
    /// </summary>
    private static readonly HashSet<string> KnownExceptions = new(StringComparer.Ordinal)
    {
        // Bookworm is a sample application, not a Trax package. The Trax prefix marks registration
        // that Trax itself ships, so a sample claiming it would teach readers to put their own
        // application code under the framework's name. These three register Bookworm's own
        // contexts and loaders, and are named after them.
        "AddCatalogDataContext",
        "AddLendingDataContext",
        "AddBookwormCrossSchema",
    };

    [Test]
    public void Public_Extension_Methods_In_ExtensionsFolders_Contain_TraxInName()
    {
        var offenders = HostingExtensions()
            .Where(e => !e.Name.Contains("Trax", StringComparison.Ordinal))
            .Where(e => !KnownExceptions.Contains(e.Name))
            .Select(e => $"{RepoRoot.Relative(e.Source)} -> {e.Name} (extends {e.Receiver})")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "Trax.Docs/reference/extension-method-naming.md requires public Add*/Use* extensions "
                    + "on IServiceCollection / IApplicationBuilder / WebApplication / "
                    + "IEndpointRouteBuilder / WebApplicationBuilder declared in an Extensions/ "
                    + "folder under samples/, lib/ or templates/ to contain 'Trax' in the method "
                    + "name (e.g. AddTraxApi, UseTraxDashboard, AddScopedTraxRoute). If a method is "
                    + "intentionally exempt, add it to ExtensionMethodNamingTests.KnownExceptions "
                    + "with a justification. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void KnownExceptions_AreNotStale()
    {
        var declared = HostingExtensions().Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var stale = KnownExceptions.Where(name => !declared.Contains(name)).ToList();

        stale
            .Should()
            .BeEmpty(
                "A KnownExceptions entry names a method this repo no longer declares as a public "
                    + "Add*/Use* hosting extension in an Extensions/ folder. Remove the entry so the "
                    + "allowlist reflects reality:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    /// <summary>
    /// Every public static Add*/Use* extension on a hosting type declared in an Extensions/ folder
    /// under <see cref="SourceRoots"/>. Both tests read it, so the allowlist is checked against
    /// exactly the set the convention applies to.
    /// </summary>
    private static IEnumerable<(string Source, string Name, string Receiver)> HostingExtensions()
    {
        foreach (var file in SourceFiles.CSharp(SourceRoots))
        {
            if (
                !file.Contains(
                    $"{Path.DirectorySeparatorChar}Extensions{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
                continue;

            var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
            var root = tree.GetCompilationUnitRoot();

            foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
                    continue;
                if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
                    continue;

                var name = method.Identifier.Text;
                if (
                    !name.StartsWith("Add", StringComparison.Ordinal)
                    && !name.StartsWith("Use", StringComparison.Ordinal)
                )
                    continue;

                var firstParam = method.ParameterList.Parameters.FirstOrDefault();
                if (firstParam is null)
                    continue;
                if (!firstParam.Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword)))
                    continue;

                var paramType = firstParam.Type?.ToString();
                if (paramType is null)
                    continue;
                if (!HostingTypes.Contains(paramType))
                    continue;

                yield return (file, name, paramType);
            }
        }
    }
}
