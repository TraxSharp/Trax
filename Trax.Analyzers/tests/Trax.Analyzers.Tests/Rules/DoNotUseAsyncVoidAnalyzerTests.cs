using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.DoNotUseAsyncVoidAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseAsyncVoidAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task Method_Void()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                void A() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Method_AsyncVoid()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                async void {|TRX0155:A|}() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Method_AsyncTask()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                async System.Threading.Tasks.Task A() => throw null;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalFunction_Void()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                void A()
                {
                  void Local() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalFunction_AsyncVoid()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                void A()
                {
                  {|TRX0155:async void Local() => throw null;|}
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task LocalFunction_AsyncTask()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                void A()
                {
                  async System.Threading.Tasks.Task Local() => throw null;
                }
            }
            """;

        return test.RunAsync();
    }
}
