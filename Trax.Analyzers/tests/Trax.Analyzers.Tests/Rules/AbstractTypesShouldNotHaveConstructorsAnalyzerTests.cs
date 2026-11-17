using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.AbstractTypesShouldNotHaveConstructorsAnalyzer,
    Trax.Analyzers.Rules.AbstractTypesShouldNotHaveConstructorsFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class AbstractTypesShouldNotHaveConstructorsAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task Ctor()
    {
        var test = CreateTest();
        test.TestCode = """
            abstract class Test
            {
                protected Test(int a) { }
                private Test(object a) { }
            }

            class Test2
            {
                public Test2() { }
                internal Test2(long a) { }
                protected Test2(int a) { }
                private Test2(object a) { }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task PublicCtor()
    {
        var test = CreateTest();
        test.TestCode = """
            abstract class Test
            {
                public {|TRX0017:Test|}() { }
            }
            """;
        test.FixedCode = """
            abstract class Test
            {
                protected Test() { }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InternalCtor()
    {
        var test = CreateTest();
        test.TestCode = """
            abstract class Test
            {
                internal {|TRX0017:Test|}() { }
            }
            """;
        test.FixedCode = """
            abstract class Test
            {
                protected Test() { }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InternalCtor_BatchFix()
    {
        var test = CreateTest();
        test.TestCode = """
            abstract class Test
            {
                internal {|TRX0017:Test|}() { }

                internal {|TRX0017:Test|}(int a) { }
            }
            """;
        test.FixedCode = """
            abstract class Test
            {
                protected Test() { }

                protected Test(int a) { }
            }
            """;

        return test.RunAsync();
    }
}
