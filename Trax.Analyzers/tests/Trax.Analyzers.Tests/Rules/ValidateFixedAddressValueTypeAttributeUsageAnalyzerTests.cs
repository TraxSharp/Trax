using Microsoft.CodeAnalysis.Testing;
using AnalyzerTest = Trax.Analyzers.Tests.Harness.CSharpAnalyzerTest<
    Trax.Analyzers.Rules.ValidateFixedAddressValueTypeAttributeUsageAnalyzer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class ValidateFixedAddressValueTypeAttributeUsageAnalyzerTests
{
    private static AnalyzerTest CreateTest() => new();

    [Fact]
    public Task ValidField()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                [System.Runtime.CompilerServices.FixedAddressValueType]
                static int _field;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task FieldMustBeStatic()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                [System.Runtime.CompilerServices.FixedAddressValueType]
                int {|TRX0207:_field|};
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task FieldTypeMustBeValueType()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                [System.Runtime.CompilerServices.FixedAddressValueType]
                static {|TRX0208:string|} _field;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task BothDiagnostics()
    {
        var test = CreateTest();
        test.TestCode = """
            class Sample
            {
                [System.Runtime.CompilerServices.FixedAddressValueType]
                {|TRX0208:string|} {|TRX0207:_field|};
            }
            """;

        return test.RunAsync();
    }
}
