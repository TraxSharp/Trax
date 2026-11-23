using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using CodeFixTest = Trax.Analyzers.Tests.Harness.CSharpCodeFixTest<
    Trax.Analyzers.Rules.DoNotUseEqualityOperatorsForSpanOfCharAnalyzer,
    Trax.Analyzers.Rules.DoNotUseEqualityOperatorsForSpanOfCharFixer>;

namespace Trax.Analyzers.Tests.Rules;

public sealed class DoNotUseEqualityOperatorsForSpanOfCharAnalyzerTests
{
    private static CodeFixTest CreateTest() => new();

    [Fact]
    public Task SpanEquals()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class Test
            {
                void A()
                {
                    _ = {|TRX0103:"a".AsSpan() == "ab".AsSpan().Slice(0, 1)|};
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Test
            {
                void A()
                {
                    _ = "a".AsSpan().SequenceEqual("ab".AsSpan().Slice(0, 1));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SpanNotEquals()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class Test
            {
                void A()
                {
                    _ = {|TRX0103:"a".AsSpan() != "ab".AsSpan().Slice(0, 1)|};
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Test
            {
                void A()
                {
                    _ = !"a".AsSpan().SequenceEqual("ab".AsSpan().Slice(0, 1));
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringEquals()
    {
        var test = CreateTest();
        test.TestCode = """
            using System;
            class Test
            {
                void A()
                {
                    _ = "a" == "ab";
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task StringEqualsSpan_CSharp12()
    {
        var test = CreateTest();
        test.LanguageVersion = LanguageVersion.CSharp12;
        test.TestCode = """
            using System;
            class Test
            {
                void A(ReadOnlySpan<char> s)
                {
                    _ = {|TRX0103:"a" == s|};
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Test
            {
                void A(ReadOnlySpan<char> s)
                {
                    _ = "a".AsSpan().SequenceEqual(s);
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task SpanNotEqualsString_CSharp12()
    {
        var test = CreateTest();
        test.LanguageVersion = LanguageVersion.CSharp12;
        test.TestCode = """
            using System;
            class Test
            {
                void A(ReadOnlySpan<char> s)
                {
                    _ = {|TRX0103:s != "a"|};
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Test
            {
                void A(ReadOnlySpan<char> s)
                {
                    _ = !s.SequenceEqual("a");
                }
            }
            """;

        return test.RunAsync();
    }

    [Fact]
    public Task CharArrayEqualsSpan_CSharp12()
    {
        var test = CreateTest();
        test.LanguageVersion = LanguageVersion.CSharp12;
        test.TestCode = """
            using System;
            class Test
            {
                void A(char[] a, ReadOnlySpan<char> s)
                {
                    _ = {|TRX0103:a == s|};
                }
            }
            """;
        test.FixedCode = """
            using System;
            class Test
            {
                void A(char[] a, ReadOnlySpan<char> s)
                {
                    _ = ((ReadOnlySpan<char>)a).SequenceEqual(s);
                }
            }
            """;

        return test.RunAsync();
    }
}
