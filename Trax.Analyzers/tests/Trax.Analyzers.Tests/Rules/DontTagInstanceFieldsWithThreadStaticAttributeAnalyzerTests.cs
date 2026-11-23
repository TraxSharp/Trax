using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DontTagInstanceFieldsWithThreadStaticAttributeAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DontTagInstanceFieldsWithThreadStaticAttributeAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task DontReport()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test2
            {
                int _a;
                [System.ThreadStatic]
                static int _b;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Report()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test2
            {
                [System.ThreadStatic]
                int {|TRX0033:_a|};
            }
            """;

        return test.RunAsync();
    }
}
