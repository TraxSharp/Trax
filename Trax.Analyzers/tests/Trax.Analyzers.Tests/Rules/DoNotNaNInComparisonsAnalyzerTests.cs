using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.DoNotNaNInComparisonsAnalyzer,
    Trax.Analyzers.Rules.DoNotNaNInComparisonsFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotNaNInComparisonsAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task Comparisons()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A()
                {
                    _ = 1d == 0d;
                    _ = 1d != 0d;
                    _ = 0d == {|TRX0082:double.NaN|};
                    _ = 0d != {|TRX0082:double.NaN|};
                    _ = {|TRX0082:double.NaN|} == 0d;
                    _ = {|TRX0082:double.NaN|} != 0d;
                    _ = 0d < {|TRX0082:double.NaN|};
                    _ = 0d <= {|TRX0082:double.NaN|};
                    _ = 0d > {|TRX0082:double.NaN|};
                    _ = 0d >= {|TRX0082:double.NaN|};
                    _ = {|TRX0082:double.NaN|} < 0d;

                    _ = 1f == 0f;
                    _ = 1f != 0f;
                    _ = 0f == {|TRX0082:float.NaN|};
                    _ = 0f != {|TRX0082:float.NaN|};
                    _ = {|TRX0082:float.NaN|} == 0f;
                    _ = {|TRX0082:float.NaN|} != 0f;

                    _ = (double){|TRX0082:float.NaN|} != 1f;

                    System.Half halfValue = (System.Half)0;
                    _ = halfValue == {|TRX0082:System.Half.NaN|};
                    _ = halfValue != {|TRX0082:System.Half.NaN|};
                    _ = {|TRX0082:System.Half.NaN|} == halfValue;
                    _ = {|TRX0082:System.Half.NaN|} != halfValue;

                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_GenericMath_CodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            using System.Numerics;
            class Test
            {
                void A<T>(T value) where T : IFloatingPointIeee754<T>
                {
                    _ = value == {|TRX0082:T.NaN|};
                }

                void B<T>(T value) where T : IBinaryFloatingPointIeee754<T>
                {
                    _ = {|TRX0082:T.NaN|} != value;
                }
            }
            """;
        test.FixedCode = """
            using System.Numerics;
            class Test
            {
                void A<T>(T value) where T : IFloatingPointIeee754<T>
                {
                    _ = T.IsNaN(value);
                }

                void B<T>(T value) where T : IBinaryFloatingPointIeee754<T>
                {
                    _ = !T.IsNaN(value);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_GenericMath_BothOperandsAreNaN_NoCodeFix()
    {
        const string Source = """
            using System.Numerics;
            class Test
            {
                void A<T>() where T : IFloatingPointIeee754<T>
                {
                    _ = {|TRX0082:T.NaN|} == {|TRX0082:T.NaN|};
                }
            }
            """;

        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_RelationalOperators_NoCodeFix()
    {
        const string Source = """
            using System;
            class Test
            {
                void A(double doubleValue, float floatValue, Half halfValue)
                {
                    _ = doubleValue < {|TRX0082:double.NaN|};
                    _ = doubleValue <= {|TRX0082:double.NaN|};
                    _ = doubleValue > {|TRX0082:double.NaN|};
                    _ = doubleValue >= {|TRX0082:double.NaN|};
                    _ = {|TRX0082:float.NaN|} < floatValue;
                    _ = halfValue >= {|TRX0082:Half.NaN|};
                }

                void B<T>(T value) where T : System.Numerics.IFloatingPointIeee754<T>
                {
                    _ = value < {|TRX0082:T.NaN|};
                    _ = value >= {|TRX0082:T.NaN|};
                }
            }
            """;

        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_CodeFix()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(double value)
                {
                    _ = value == {|TRX0082:double.NaN|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(double value)
                {
                    _ = double.IsNaN(value);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_CodeFix_Float()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                void A(float value)
                {
                    _ = value != {|TRX0082:float.NaN|};
                }
            }
            """;
        test.FixedCode = """
            class Test
            {
                void A(float value)
                {
                    _ = !float.IsNaN(value);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_BothOperandsAreNaN_NoCodeFix()
    {
        const string Source = """
            class Test
            {
                void A()
                {
                    _ = {|TRX0082:double.NaN|} == {|TRX0082:double.NaN|};
                    _ = {|TRX0082:double.NaN|} != {|TRX0082:double.NaN|};
                    _ = (double){|TRX0082:float.NaN|} == {|TRX0082:double.NaN|};
                }
            }
            """;

        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_CodeFix_Half()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class Test
            {
                void A(Half value)
                {
                    _ = value == {|TRX0082:Half.NaN|};
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Test
            {
                void A(Half value)
                {
                    _ = Half.IsNaN(value);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_CodeFix_NaNOfAnotherType()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                bool A(double value) => value == {|TRX0082:float.NaN|};
                bool B(float value) => value != {|TRX0082:double.NaN|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                bool A(double value) => double.IsNaN(value);
                bool B(float value) => !double.IsNaN(value);
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_CodeFix_NullableOperand()
    {
        var test = CreateTest();
        test.TestCode = """
            class Test
            {
                bool A(double? value) => value == {|TRX0082:double.NaN|};
                bool B(double? value) => {|TRX0082:float.NaN|} != value;
                bool C(double? a, double? b) => (a ?? b) == {|TRX0082:double.NaN|};
            }
            """;
        test.FixedCode = """
            class Test
            {
                bool A(double? value) => double.IsNaN(value.GetValueOrDefault());
                bool B(double? value) => !double.IsNaN(value.GetValueOrDefault());
                bool C(double? a, double? b) => double.IsNaN((a ?? b).GetValueOrDefault());
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Comparisons_UserDefinedOperator_NoCodeFix()
    {
        const string Source = """
            class Sample
            {
                public static bool operator ==(Sample a, double b) => false;
                public static bool operator !=(Sample a, double b) => true;
                public override bool Equals(object o) => false;
                public override int GetHashCode() => 0;

                bool A(Sample value) => value == {|TRX0082:double.NaN|};
            }
            """;

        var test = CreateTest();
        test.TestCode = Source;
        test.FixedCode = Source;

        return test.RunAsync();
    }
}
