using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Dashboard.Tests.Meta.Tests;

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
        // Legacy compat shim from before the AddTrax fluent API; called internally by AddMediator.
        // Kept public for users still on the old API.
        "AddServiceTrainBus",
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
                if (name.Contains("Trax", StringComparison.Ordinal))
                    continue;
                if (KnownExceptions.Contains(name))
                    continue;

                offenders.Add($"{RepoRoot.Relative(file)} -> {name} (extends {paramType})");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "CLAUDE.md > Extension Method Naming Convention requires public Add*/Use* extensions "
                    + "on IServiceCollection / IApplicationBuilder / WebApplication / "
                    + "IEndpointRouteBuilder / WebApplicationBuilder declared in any src/*/Extensions/ "
                    + "folder to contain 'Trax' in the method name (e.g. AddTraxApi, UseTraxDashboard, "
                    + "AddScopedTraxRoute). If a method is intentionally exempt, add it to "
                    + "ExtensionMethodNamingTests.KnownExceptions with a justification. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
