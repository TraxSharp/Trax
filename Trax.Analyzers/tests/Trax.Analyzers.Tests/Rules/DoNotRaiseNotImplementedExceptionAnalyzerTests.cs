#pragma warning disable CA1030 // Use events where appropriate

using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DoNotRaiseNotImplementedExceptionAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotRaiseNotImplementedExceptionAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task RaiseNotReservedException_ShouldNotReportErrorAsync()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class TestAttribute
            {
                void Test()
                {
                    throw new Exception();

                    try
                    {
                    }
                    catch (NotImplementedException)
                    {
                        throw;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task RaiseNotImplementedException_ShouldReportErrorAsync()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class TestAttribute
            {
                void Test()
                {
                    {|TRX0025:throw new NotImplementedException();|}
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task RaiseNotImplementedException_FlowedFromLocal_ShouldReportErrorAsync()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class TestAttribute
            {
                void Test()
                {
                    Exception exception = new NotImplementedException();
                    {|TRX0025:throw exception;|}
                }
            }
            """;

        return test.RunAsync();
    }
}
