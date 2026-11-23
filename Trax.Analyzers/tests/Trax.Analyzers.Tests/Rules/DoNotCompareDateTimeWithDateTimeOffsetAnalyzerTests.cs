using Microsoft.CodeAnalysis;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DoNotImplicitlyConvertDateTimeToDateTimeOffsetAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public class DoNotCompareDateTimeWithDateTimeOffsetAnalyzerTests
{
    private static AnalyzerTest CreateTest()
    {
        var test = new AnalyzerTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        return test;
    }

    [Fact]
    public Task ImplicitConversion_BinaryOperation_Subtract_UtcNow()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;

            _ = {|TRX0133:DateTime.UtcNow|} - DateTimeOffset.UtcNow;
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ImplicitConversion_BinaryOperation_Subtract()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;

            _ = {|TRX0132:default(DateTime)|} - DateTimeOffset.UtcNow;
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ExplicitConversion_BinaryOperation_Subtract()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;

            _ = (DateTimeOffset)default(DateTime) - DateTimeOffset.UtcNow;
            """;

        return test.RunAsync();
    }
}
