using AwesomeAssertions;
using Trax.Effect.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

/// <summary>
/// A stored type name that parses but names a type that cannot exist resolves to nothing, as the
/// resolver's contract says, rather than throwing out of a JSON read or a manifest load.
/// </summary>
[TestFixture]
public class LoadedTypeResolverTests
{
    [TestCase("System.Void[]")]
    [TestCase("System.Span`1[[System.Int32]][]")]
    [TestCase("System.ReadOnlySpan`1[[System.Byte]][,]")]
    [TestCase("System.TypedReference[]")]
    [TestCase("System.Collections.Generic.List`1[[System.Void]]")]
    [TestCase("System.Collections.Generic.List`1[[System.Span`1[[System.Int32]]]]")]
    public void A_name_no_type_can_have_resolves_to_null(string name)
    {
        LoadedTypeResolver.Resolve(name, allowAssemblyQualified: false).Should().BeNull();
        LoadedTypeResolver.Resolve(name, allowAssemblyQualified: true).Should().BeNull();
    }

    [Test]
    public void An_array_of_rank_above_32_resolves_to_null()
    {
        var name = "System.Int32[" + new string(',', 32) + "]";

        LoadedTypeResolver.Resolve(name, allowAssemblyQualified: false).Should().BeNull();
    }

    [TestCase("System.Int32[]", typeof(int[]))]
    [TestCase("System.Int32[,]", typeof(int[,]))]
    [TestCase("System.Collections.Generic.List`1[[System.String]][]", typeof(List<string>[]))]
    public void An_array_of_a_loaded_type_still_resolves(string name, Type expected) =>
        LoadedTypeResolver.Resolve(name, allowAssemblyQualified: false).Should().Be(expected);
}
