using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.DoNotUseEqualityComparerDefaultOfStringAnalyzer,
    Trax.Analyzers.Rules.DoNotUseEqualityComparerDefaultOfStringFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseEqualityComparerDefaultOfStringAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task TestAsync()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Collections.Generic;
            class Test
            {
                internal void Sample()
                {
                    _ = EqualityComparer<int>.Default.Equals(0, 0);
                    _ = {|TRX0024:EqualityComparer<string>.Default|}.Equals(null, null);
                }
            }
            """;
        test.FixedCode = """
            using System.Collections.Generic;
            class Test
            {
                internal void Sample()
                {
                    _ = EqualityComparer<int>.Default.Equals(0, 0);
                    _ = System.StringComparer.Ordinal.Equals(null, null);
                }
            }
            """;

        return test.RunAsync();
    }
}
