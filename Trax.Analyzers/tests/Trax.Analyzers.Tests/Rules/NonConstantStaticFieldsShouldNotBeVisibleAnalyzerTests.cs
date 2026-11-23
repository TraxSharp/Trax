using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.NonConstantStaticFieldsShouldNotBeVisibleAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class NonConstantStaticFieldsShouldNotBeVisibleAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task ReportDiagnostic()
    {
        var test = CreateTest();
        test.TestCode = """
            public class Sample
            {
                public static int {|TRX0069:a|} = 0;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InternalClass()
    {
        var test = CreateTest();
        test.TestCode = """
            internal class Sample
            {
                public static int a = 0;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StaticReadOnly()
    {
        var test = CreateTest();
        test.TestCode = """
            public class Sample
            {
                public static readonly int a = 0;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task InstanceField()
    {
        var test = CreateTest();
        test.TestCode = """
            public class Sample
            {
                public int a = 0;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task EnumMembers()
    {
        var test = CreateTest();
        test.TestCode = """
            public enum Sample
            {
                A = 1,
                B,
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Const()
    {
        var test = CreateTest();
        test.TestCode = """
            public class Sample
            {
                public const int a = 0;
            }
            """;

        return test.RunAsync();
    }
}
