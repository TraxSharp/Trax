using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.PrimaryConstructorParameterShouldBeReadOnlyAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class PrimaryConstructorParameterShouldBeReadOnlyAnalyzerTests
{
    private static AnalyzerTest CreateTest()
    {
        var test = new AnalyzerTest();
        return test;
    }

    [Fact]
    public Task AssignClassicCtorParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                Test(int p) => p++;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AssignClassicParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(int p) => p++;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AssignUsingIncrementOperator()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int p)
            {
                int A() => {|TRX0143:p|}++;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AssignUsingDecrementOperator()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int p)
            {
                int A() => {|TRX0143:p|}--;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AssignUsingInfixDecrementOperator()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int p)
            {
                int A() => --{|TRX0143:p|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task DeconstructionAssignment()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int p)
            {
                void A()
                {
                    ({|TRX0143:p|}, _) = (1, 0);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Deconstruction_Deep_Assignment()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int p)
            {
                void A()
                {
                    (var a, ({|TRX0143:p|}, _)) = (0, (1, 2));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task CoalesceAssignment()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A()
                {
                    {|TRX0143:p|} ??= "";
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task CompoundAssignment()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A()
                {
                    {|TRX0143:p|} += "";
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task AssignVariable()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A()
                {
                    var a = p;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Argument()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A(string value)
                {
                    A(p);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EditUsingRefVariable()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A()
                {
                    ref var a = ref {|TRX0143:p|};
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EditUsingRefParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A(ref string a)
                {
                    A(ref {|TRX0143:p|});
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EditUsingInParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(string p)
            {
                void A(in string a)
                {
                    A(in p);
                }
            }
            """;

        return test.RunAsync();
    }
}
