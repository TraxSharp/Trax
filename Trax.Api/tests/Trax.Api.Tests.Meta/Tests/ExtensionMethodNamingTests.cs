using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Api.Tests.Meta.Tests;

/// <summary>
/// Public extension methods in Extensions/ folders carry Trax in the name.
///
/// <para>Not ADR-enforcing: it pins a naming convention that was adopted rather than chosen, and nothing about it was a trade-off between real options.</para>
/// </summary>
[TestFixture]
public class ExtensionMethodNamingTests
{
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
        // PersistedOperations sub-config helpers: intentionally non-Trax-prefixed because they
        // live in the PersistedOperations sub-package and target the feature, not the framework.
        // Reached via UseTraxGraphQL(...).UsePersistedOperations(...) in the canonical flow.
        "AddPersistedOperationStore",
        "UsePersistedOperationsEnforcement",
    };

    [Test]
    public void Public_Extension_Methods_In_ExtensionsFolders_Contain_TraxInName()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles.CSharp("src"))
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
                var offence = Offence(file, method);
                if (offence is not null)
                    offenders.Add(offence);
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "Trax.Docs/contributing/api-conventions.md > Extension method naming requires public Add*/Use* extensions "
                    + "on IServiceCollection / IApplicationBuilder / WebApplication / "
                    + "IEndpointRouteBuilder / WebApplicationBuilder declared in any src/*/Extensions/ "
                    + "folder to contain 'Trax' in the method name (e.g. AddTraxApi, UseTraxApiDashboard, "
                    + "AddScopedTraxRoute). If a method is intentionally exempt, add it to "
                    + "ExtensionMethodNamingTests.KnownExceptions with a justification. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static string? Offence(string file, MethodDeclarationSyntax method)
    {
        if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))
            return null;
        if (!method.Modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword)))
            return null;

        var name = method.Identifier.Text;
        if (
            !name.StartsWith("Add", StringComparison.Ordinal)
            && !name.StartsWith("Use", StringComparison.Ordinal)
        )
            return null;

        var firstParam = method.ParameterList.Parameters.FirstOrDefault();
        if (firstParam is null)
            return null;
        if (!firstParam.Modifiers.Any(m => m.IsKind(SyntaxKind.ThisKeyword)))
            return null;

        var paramType = firstParam.Type?.ToString();
        if (paramType is null)
            return null;

        if (!HostingTypes.Contains(paramType))
            return null;
        if (name.Contains("Trax", StringComparison.Ordinal))
            return null;
        if (KnownExceptions.Contains(name))
            return null;

        return $"{RepoRoot.Relative(file)} -> {name} (extends {paramType})";
    }
}
