using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DoNotUseFinalizerAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseFinalizerAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task TestFinalizerReportError()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                public Test() { }
                internal void A() { }

                ~{|TRX0055:Test|}()
                {
                }
            }
            """;

        return test.RunAsync();
    }
}
