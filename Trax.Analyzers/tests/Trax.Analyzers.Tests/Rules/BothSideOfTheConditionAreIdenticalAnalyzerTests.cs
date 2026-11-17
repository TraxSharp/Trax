using Microsoft.CodeAnalysis;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.BothSideOfTheConditionAreIdenticalAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class BothSideOfTheConditionAreIdenticalAnalyzerTests
{
    private static AnalyzerTest CreateTest()
    {
        var test = new AnalyzerTest();
        test.TestState.OutputKind = OutputKind.ConsoleApplication;
        return test;
    }

    [Theory]
    [InlineData("a == b")]
    [InlineData("a != b")]
    [InlineData("a & b")]
    [InlineData("a && b")]
    [InlineData("a | b")]
    [InlineData("a || b")]
    [InlineData("a is false")]
    [InlineData("a is true")]
    [InlineData("a is false or true")]
    [InlineData("a is false and not true")]
    public Task DifferentCode(string expression)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            var a = false;
            var b = false;
            var c = 0;
            _ = {{expression}};
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("{|TRX0172:a == a|}")]
    [InlineData("{|TRX0172:a != a|}")]
    [InlineData("{|TRX0172:a & a|}")]
    [InlineData("{|TRX0172:a && a|}")]
    [InlineData("{|TRX0172:a | a|}")]
    [InlineData("{|TRX0172:a || a|}")]
    [InlineData("a is {|TRX0172:true or true|}")]
    [InlineData("a is {|TRX0172:true and true|}")]
    public Task SameCode(string expression)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            var a = false;
            var b = false;
            var c = 0;
            _ = {{expression}};
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("e.MoveNext() && e.MoveNext()")]
    [InlineData("M() == M()")]
    [InlineData("c++ == c++")]
    [InlineData("(c = 1) == (c = 1)")]
    [InlineData("new object() == new object()")]
    [InlineData("await t && await t")]
    public Task SameCodeWithSideEffects(string expression)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            var c = 0;
            var e = new System.Collections.Generic.List<int>().GetEnumerator();
            var t = System.Threading.Tasks.Task.FromResult(true);
            _ = {{expression}};

            static bool M() => true;
            """;

        return test.RunAsync();
    }
}
