using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.UseEventArgsEmptyAnalyzer,
    Trax.Analyzers.Rules.UseEventArgsEmptyFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class UseEventArgsEmptyAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task ShouldReport()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                public void Test()
                {
                    _ = {|TRX0019:new System.EventArgs()|};
                }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                public void Test()
                {
                    _ = System.EventArgs.Empty;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Attribute_ShouldNotReport()
    {
        var test = CreateTest();
        test.TestCode = """
            [System.EventArgs]
            class TypeName
            {
            }
            """;
        test.CompilerDiagnostics = CompilerDiagnostics.None;

        return test.RunAsync();
    }

    [Fact]
    public Task Attribute_InvalidCode_ShouldNotReport()
    {
        var test = CreateTest();
        test.TestCode = """
            delegate d Fi[ System.EventArgs
            """;
        test.CompilerDiagnostics = CompilerDiagnostics.None;

        return test.RunAsync();
    }
}
