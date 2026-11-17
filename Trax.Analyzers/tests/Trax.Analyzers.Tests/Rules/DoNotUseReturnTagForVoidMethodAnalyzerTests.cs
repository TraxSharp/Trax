using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DoNotUseReturnTagForVoidMethodAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseReturnTagForVoidMethodAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Theory]
    [InlineData("returns")]
    [InlineData("return")]
    public Task VoidMethod_ReturnTag(string tag)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            class Sample
            {
                /// <summary>Does something.</summary>
                /// {|TRX0203:<{{tag}}>|}The result.</{{tag}}>
                void M()
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidMethod_EmptyReturnTag()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                /// <summary>Does something.</summary>
                /// {|TRX0203:<returns />|}
                void M()
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NonVoidMethod_ReturnTag()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                /// <summary>Gets a value.</summary>
                /// <returns>The result.</returns>
                int M() => 0;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task VoidMethod_NoReturnTag()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                /// <summary>Does something.</summary>
                void M()
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Constructor_ReturnTag()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                /// <summary>Initializes a new instance.</summary>
                /// <returns>The result.</returns>
                public Sample()
                {
                }
            }
            """;

        return test.RunAsync();
    }
}
