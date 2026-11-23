using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.TypeNameMustNotMatchNamespaceAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class TypeNameMustNotMatchNamespaceAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task DifferentName()
    {
        var test = CreateTest();
        test.TestCode = """
            namespace TestNamespace
            {
                class TestClass
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SameName()
    {
        var test = CreateTest();
        test.TestCode = """
            namespace Test
            {
                class {|TRX0049:Test|}
                {
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SameNameInNestedType()
    {
        var test = CreateTest();
        test.TestCode = """
            namespace Test
            {
                class TestClass
                {
                    class Test
                    {
                    }
                }
            }
            """;

        return test.RunAsync();
    }
}
