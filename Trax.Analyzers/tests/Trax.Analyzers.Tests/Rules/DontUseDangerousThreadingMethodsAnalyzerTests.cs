using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DontUseDangerousThreadingMethodsAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DontUseDangerousThreadingMethodsAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Theory]
    [InlineData("Thread.CurrentThread.Abort()")]
    [InlineData("Thread.CurrentThread.Suspend()")]
    [InlineData("Thread.CurrentThread.Resume()")]
    public Task ReportDiagnostic(string text)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            using System.Threading;
            public class Test
            {
                public void A()
                {
                    {|TRX0035:{{text}}|};
                }
            }
            """;

        return test.RunAsync();
    }
}
