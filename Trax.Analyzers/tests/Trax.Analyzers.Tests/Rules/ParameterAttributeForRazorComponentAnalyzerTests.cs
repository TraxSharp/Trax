using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.ParameterAttributeForRazorComponentAnalyzer,
    Trax.Analyzers.Rules.ParameterAttributeForRazorComponentFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class ParameterAttributeForRazorComponentAnalyzerTests
{
    private static CodeFixTest CreateTest()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = ReferenceAssemblies.Net.Net60.AddAspNetCore("6.0.10");
        return test;
    }

    [Fact]
    public Task SupplyParameterFromQuery_MissingParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            [Route("/test")]
            class Test
            {
                [SupplyParameterFromQuery]
                public int {|TRX0116:A|} { get; set; }
            }
            """;
        test.FixedCode = """
            using Microsoft.AspNetCore.Components;

            [Route("/test")]
            class Test
            {
                [SupplyParameterFromQuery]
                [Parameter]
                public int A { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SupplyParameterFromQuery_MissingParameter_AspNetCore8()
    {
        var test = CreateTest();
        test.ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddAspNetCore("8.0.0");
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            [Route("/test")]
            class Test
            {
                [SupplyParameterFromQuery]
                public int A { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SupplyParameterFromQuery_WithParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            [Route("/test")]
            class Test
            {
                [Parameter]
                [SupplyParameterFromQuery]
                public int A { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SupplyParameterFromQuery_WithCascadingParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            [Route("/test")]
            class Test
            {
                [CascadingParameter]
                [SupplyParameterFromQuery]
                public int {|TRX0116:A|} { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SupplyParameterFromQuery_NonRoutable()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            class Test
            {
                [Parameter, SupplyParameterFromQuery]
                public int {|TRX0122:A|} { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EditorRequired_MissingParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            class Test
            {
                [EditorRequired]
                public int {|TRX0117:A|} { get; set; }
            }
            """;
        test.FixedCode = """
            using Microsoft.AspNetCore.Components;

            class Test
            {
                [EditorRequired]
                [Parameter]
                public int A { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EditorRequired_WithParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            class Test
            {
                [Parameter]
                [EditorRequired]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EditorRequired_WithCascadingParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            using Microsoft.AspNetCore.Components;

            class Test
            {
                [CascadingParameter]
                [EditorRequired]
                public int {|TRX0117:A|} { get; set; }

                public int B { get; set; }
            }
            """;

        return test.RunAsync();
    }

#if ROSLYN_5_6_OR_GREATER
    // The older versions of the parser do not produce the same syntax tree for this invalid code
    [Fact]
    [Trait("Issue", "https://github.com/meziantou/Meziantou.Analyzer/issues/1672")]
    public Task InvalidCode_EditorRequired_PropertyWithoutName_NoCodeFix()
    {
        const string Source = """
            using Microsoft.AspNetCore.Components;

            class Test
            {
                [EditorRequired]
                public int {|TRX0117:|}{ get; set; }
            }
            """;
        var test = CreateTest();
        // The code does not compile, so the compiler diagnostics cannot be verified
        test.CompilerDiagnostics = CompilerDiagnostics.None;
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }
#endif
}
