using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.DoNotUseStringGetHashCodeAnalyzer,
    Trax.Analyzers.Rules.DoNotUseStringGetHashCodeFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseStringGetHashCodeAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task GetHashCode_ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class TypeName
            {
                public void Test()
                {
                    {|TRX0021:"a".GetHashCode()|};
                    System.StringComparer.Ordinal.GetHashCode("a");
                    new object().GetHashCode();
                }
            }
            """;
        test.FixedCode = """
            class TypeName
            {
                public void Test()
                {
                    System.StringComparer.Ordinal.GetHashCode("a");
                    System.StringComparer.Ordinal.GetHashCode("a");
                    new object().GetHashCode();
                }
            }
            """;

        return test.RunAsync();
    }
}
