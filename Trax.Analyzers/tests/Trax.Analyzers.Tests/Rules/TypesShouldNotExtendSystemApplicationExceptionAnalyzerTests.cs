using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.TypesShouldNotExtendSystemApplicationExceptionAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class TypesShouldNotExtendSystemApplicationExceptionAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task InheritFromException_ShouldNotReportError()
    {
        var test = CreateTest();
        test.TestCode = "class Test : System.Exception { }";

        return test.RunAsync();
    }

    [Fact]
    public Task InheritFromApplicationException_ShouldReportError()
    {
        var test = CreateTest();
        test.TestCode = "class {|TRX0013:Test|} : System.ApplicationException { }";

        return test.RunAsync();
    }
}
