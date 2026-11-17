using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.DoNotRemoveOriginalExceptionFromThrowStatementAnalyzer,
    Trax.Analyzers.Rules.DoNotRemoveOriginalExceptionFromThrowStatementFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotRemoveOriginalExceptionFromThrowStatementAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task NoDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                internal void Sample()
                {
                    throw new System.Exception();

                    try
                    {
                        throw new System.Exception();
                    }
                    catch (System.Exception ex)
                    {
                        throw new System.Exception("test", ex);
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ShouldReportDiagnostic_DerivedException()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                internal void Sample()
                {
                    try
                    {
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        {|TRX0027:throw ex;|}
                    }
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                internal void Sample()
                {
                    try
                    {
                    }
                    catch (System.InvalidOperationException ex)
                    {
                        throw;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ShouldReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                internal void Sample()
                {
                    try
                    {
                    }
                    catch (System.Exception ex)
                    {
                        _ = ex;
                        {|TRX0027:throw ex;|}
                    }
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                internal void Sample()
                {
                    try
                    {
                    }
                    catch (System.Exception ex)
                    {
                        _ = ex;
                        throw;
                    }
                }
            }
            """;

        return test.RunAsync();
    }
}
