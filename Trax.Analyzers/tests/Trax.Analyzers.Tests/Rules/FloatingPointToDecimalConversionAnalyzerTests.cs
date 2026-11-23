using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.FloatingPointToDecimalConversionAnalyzer,
    Trax.Analyzers.Rules.FloatingPointToDecimalConversionFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class FloatingPointToDecimalConversionAnalyzerTests
{
    [Fact]
    public Task Valid()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                void Test(int i, long l, decimal m)
                {
                    _ = 2.3m;
                    _ = (decimal)1;
                    _ = (decimal)i;
                    _ = (double)m;
                    _ = (float)2.3m;
                    _ = new decimal(1);
                    _ = new decimal(l);
                    _ = System.Convert.ToDecimal(1);
                    _ = System.Convert.ToDecimal("2.3");
                    _ = decimal.CreateChecked(1);
                    _ = double.CreateChecked(2.3m);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_DoubleLiteral()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                decimal Test() => {|TRX0244:(decimal)2.3|};
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                decimal Test() => 2.3m;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_Suffixes()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                void Test()
                {
                    _ = {|TRX0244:(decimal)14.1f|};
                    _ = {|TRX0244:(decimal)14.1F|};
                    _ = {|TRX0244:(decimal)2.3d|};
                    _ = {|TRX0244:(decimal)2.3D|};
                    _ = {|TRX0244:(decimal)1d|};
                    _ = {|TRX0244:(decimal)1e-3|};
                    _ = {|TRX0244:(decimal)1E+3d|};
                    _ = {|TRX0244:(decimal)1_000.5|};
                    _ = {|TRX0244:(decimal).5|};
                }
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                void Test()
                {
                    _ = 14.1m;
                    _ = 14.1m;
                    _ = 2.3m;
                    _ = 2.3m;
                    _ = 1m;
                    _ = 1e-3m;
                    _ = 1E+3m;
                    _ = 1_000.5m;
                    _ = .5m;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_Negative()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                void Test(decimal value)
                {
                    _ = {|TRX0244:(decimal)-0.5|};
                    _ = {|TRX0244:(decimal)(-0.5)|};
                    _ = {|TRX0244:(decimal)(-(0.5))|};
                    _ = value-{|TRX0244:(decimal)-0.5|};
                    _ = value - {|TRX0244:(decimal)-0.5|};
                }
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                void Test(decimal value)
                {
                    _ = -0.5m;
                    _ = -0.5m;
                    _ = -0.5m;
                    _ = value- -0.5m;
                    _ = value - -0.5m;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_Parenthesized()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                string Test() => ({|TRX0244:(decimal)(2.3)|}).ToString();
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                string Test() => (2.3m).ToString();
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_NullableDecimal()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                void Test()
                {
                    var value = {|TRX0244:(decimal?)2.3|};
                }
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                void Test()
                {
                    var value = (decimal?)2.3m;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_ConstDeclaration()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                const decimal Value = {|TRX0244:(decimal)2.3|};
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                const decimal Value = 2.3m;
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task Cast_NonLiteralConstant_NoFix()
    {
        var test = new CodeFixTest();
        var sourceCode = """
            class Dummy
            {
                const double Rate = 0.23;

                void Test()
                {
                    _ = {|TRX0244:(decimal)Rate|};
                    _ = {|TRX0244:(decimal)(1.0 / 3)|};
                    _ = {|TRX0244:(decimal)(float)2.3|};
                    _ = {|TRX0244:(decimal)+2.3|};
                }
            }
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task Constructor()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                void Test()
                {
                    _ = {|TRX0244:new decimal(123.4567)|};
                    _ = {|TRX0244:new decimal(14.1f)|};
                    decimal value = {|TRX0244:new(2.3)|};
                    _ = {|TRX0244:new decimal(-1.5)|}.ToString();
                }
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                void Test()
                {
                    _ = 123.4567m;
                    _ = 14.1m;
                    decimal value = 2.3m;
                    _ = (-1.5m).ToString();
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task ConvertToDecimal()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            using System;

            class Dummy
            {
                void Test()
                {
                    _ = {|TRX0244:Convert.ToDecimal(0.1)|};
                    _ = {|TRX0244:Convert.ToDecimal(0.1f)|};
                }
            }
            """;
        test.FixedCode = """
            using System;

            class Dummy
            {
                void Test()
                {
                    _ = 0.1m;
                    _ = 0.1m;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task GenericMath()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                void Test()
                {
                    _ = {|TRX0244:decimal.CreateChecked(2.3)|};
                    _ = {|TRX0244:decimal.CreateSaturating(2.3f)|};
                    _ = {|TRX0244:decimal.CreateTruncating<double>(2.3)|};
                }
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                void Test()
                {
                    _ = 2.3m;
                    _ = 2.3m;
                    _ = 2.3m;
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task FixAll()
    {
        var test = new CodeFixTest();
        test.TestCode = """
            class Dummy
            {
                decimal[] Test() => [{|TRX0244:(decimal)1.1|}, {|TRX0244:new decimal(2.2)|}, {|TRX0244:System.Convert.ToDecimal(3.3)|}];
            }
            """;
        test.FixedCode = """
            class Dummy
            {
                decimal[] Test() => [1.1m, 2.2m, 3.3m];
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task NonConstant()
    {
        var test = new CodeFixTest();
        var sourceCode = """
            using System;

            class Dummy
            {
                void Test(double d, float f, double? nd)
                {
                    _ = {|TRX0245:(decimal)d|};
                    _ = {|TRX0245:(decimal)f|};
                    _ = {|TRX0245:(decimal?)nd|};
                    _ = {|TRX0245:(decimal)nd|};
                    _ = {|TRX0245:new decimal(d)|};
                    _ = {|TRX0245:Convert.ToDecimal(f)|};
                    _ = {|TRX0245:decimal.CreateChecked(d)|};
                }
            }
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task XunitInlineData()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddXunitV3();
        var sourceCode = """
            using Xunit;

            public class Tests
            {
                [Theory]
                [InlineData({|TRX0244:2.3|}, 2.3, "2.3", 1)]
                [InlineData({|TRX0244:-2.3f|}, 2.3, "2.3", 1)]
                [InlineData(null, 2.3, "2.3", 1)]
                public void Test(decimal? value, double d, decimal s, decimal i)
                {
                }
            }
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task XunitInlineData_ExplicitArray()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddXunitV3();
        var sourceCode = """
            using Xunit;

            public class Tests
            {
                [Theory]
                [InlineData(new object[] { 1, {|TRX0244:2.3|} })]
                public void Test(int a, decimal b)
                {
                }
            }
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task NUnitTestCase()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddNUnit();
        var sourceCode = """
            using NUnit.Framework;

            public class Tests
            {
                [TestCase({|TRX0244:2.3|})]
                [TestCase(1, {|TRX0244:2.3|})]
                [TestCase(1, 2, {|TRX0244:2.3|})]
                [TestCase(1, 2, 3, {|TRX0244:2.3|})]
                public void Test(decimal a, decimal b = 0, decimal c = 0, decimal d = 0)
                {
                }
            }
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task MSTestDataRow()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddMSTest();
        var sourceCode = """
            using Microsoft.VisualStudio.TestTools.UnitTesting;

            [TestClass]
            public class Tests
            {
                [TestMethod]
                [DataRow({|TRX0244:2.3|})]
                [DataRow({|TRX0244:2.3|}, {|TRX0244:4.5|})]
                public void Test(decimal a, decimal b = 0)
                {
                }
            }
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task TestDataAttribute_DerivedAttribute()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddNUnit();
        var sourceCode = """
            using NUnit.Framework;

            public class Tests
            {
                [CustomTestCase({|TRX0244:2.3|})]
                public void Test(decimal value)
                {
                }
            }

            public sealed class CustomTestCaseAttribute(params object[] arguments) : TestCaseAttribute(arguments);
            """;
        test.TestCode = sourceCode;
        test.FixedCode = sourceCode;

        return test.RunAsync();
    }

    [Fact]
    public Task TestDataAttribute_ParamsParameter()
    {
        var test = new CodeFixTest();
        test.ReferenceAssemblies = test.ReferenceAssemblies.AddXunitV3();
        test.TestCode = """
            using Xunit;

            public class Tests
            {
                [Theory]
                [InlineData(2.3, 4.5)]
                public void Test(params decimal[] values)
                {
                }
            }
            """;

        return test.RunAsync();
    }
}
