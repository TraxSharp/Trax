using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.LocalVariablesShouldNotHideSymbolsAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class LocalVariablesShouldNotHideSymbolsAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task LocalVariableHideField()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                string a;

                void A()
                {
                    var {|TRX0084:a|} = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableHideProperty()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                string Prop {get;set;}

                void A()
                {
                    var {|TRX0084:Prop|} = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableHideVisibleFieldFromParentClass()
    {
        var test = CreateTest();
        test.TestCode = """
            class Base
            {
                protected string a;
            }

            class Test : Base
            {
                void A()
                {
                    var {|TRX0084:a|} = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableHidePrimaryConstructorParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int a)
            {
                void A()
                {
                    var {|TRX0084:a|} = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableDoesNotHidePrimaryConstructorParameterInStaticMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int a)
            {
                static void A()
                {
                    var a = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Theory]
    [InlineData("static int P { get { var a = 10; return a; } }")]
    [InlineData("static System.Func<int> F = () => { var a = 10; return a; };")]
    [InlineData("Test(string s) : this(Get(out var a)) { } static int Get(out int value) => value = 0;")]
    public Task LocalVariableDoesNotHidePrimaryConstructorParameterInStaticContext(string member)
    {
        var test = CreateTest();
        test.TestCode = $$"""
            class Test(int a)
            {
                {{member}}
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableHidePrimaryConstructorParameterInFieldInitializer()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int a)
            {
                System.Func<int> F = () => { var {|TRX0084:a|} = 10; return a; };
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableDoesNotHidePrimaryConstructorParameter()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test(int a)
            {
                void A()
                {
                    var b = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableHideNotVisibleFieldFromParentClass()
    {
        var test = CreateTest();
        test.TestCode = """
            class Base
            {
                private string a;
            }

            class Test : Base
            {
                void A()
                {
                    var a = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableDoesNotHideSymbol()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    var a = 10;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableInLocalFunctionHidesLocalVariableFromContainingMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    var a = 10;

                    void LocalFunction()
                    {
                        var {|TRX0084:a|} = 10;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableInStaticLocalFunctionDoesNotHideLocalVariableFromContainingMethod()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    var a = 10;

                    static void LocalFunction()
                    {
                        var a = 10;
                    }
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalVariableInNestedLocalFunctionDoesNotHideLocalVariableAcrossStaticLocalFunction()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    var a = 10;

                    static void LocalFunction()
                    {
                        void NestedLocalFunction()
                        {
                            var a = 10;
                        }
                    }
                }
            }
            """;

        return test.RunAsync();
    }
}
