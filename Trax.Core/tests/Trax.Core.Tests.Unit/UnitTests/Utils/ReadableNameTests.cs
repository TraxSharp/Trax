using AwesomeAssertions;
using Trax.Core.Utils;

namespace Trax.Core.Tests.Unit.UnitTests.Utils;

/// <summary>
/// How a type is written in a message: the way a developer would write it in C#.
/// </summary>
public class ReadableNameTests : TestSetup
{
    [TestCase(typeof(string), "String")]
    [TestCase(typeof(List<int>), "List<Int32>")]
    [TestCase(typeof(Dictionary<string, List<int>>), "Dictionary<String, List<Int32>>")]
    [TestCase(typeof(Plain), "Plain")]
    [TestCase(typeof(Outer<int>.Inner), "Outer<Int32>.Inner")]
    [TestCase(typeof(Outer<int>.Pair<string>), "Outer<Int32>.Pair<String>")]
    [TestCase(typeof(Wrapper.Box<string>), "Box<String>")]
    public void ReadableName_WritesTheTypeAsCSharpDoes(Type type, string expected) =>
        type.ReadableName().Should().Be(expected);

    public class Plain;

    public class Outer<T>
    {
        public class Inner;

        public class Pair<TOther>;
    }

    public class Wrapper
    {
        public class Box<T>;
    }
}
