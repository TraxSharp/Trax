using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.ShapeDiff;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// Binding fixture for the shape-diff guardrail. Each pair of documents is
/// labeled "same shape" or "different shape" and exercises one specific
/// canonicalization rule. New rules MUST add new pairs here.
/// </summary>
[TestFixture]
public class ShapeFingerprintComputerTests
{
    private static string Fp(string doc, string? op = null) =>
        ShapeFingerprintComputer.Compute(doc, op);

    // ---------- SAME SHAPE PAIRS ----------

    [TestCase(
        "Whitespace and indentation",
        "query Q { user { id name } }",
        "query Q {\n  user {\n    id\n    name\n  }\n}"
    )]
    [TestCase(
        "Field order within sibling set",
        "query Q { user { id name email } }",
        "query Q { user { email id name } }"
    )]
    [TestCase(
        "Argument value change (where filter)",
        "query Q { users(where: { active: true }) { id } }",
        "query Q { users(where: { active: false }) { id } }"
    )]
    [TestCase("Argument added", "query Q { users { id } }", "query Q { users(first: 10) { id } }")]
    [TestCase(
        "Pagination size change",
        "query Q { users(first: 10) { id } }",
        "query Q { users(first: 100) { id } }"
    )]
    [TestCase(
        "Order direction reversed",
        "query Q { users(order: { name: ASC }) { id } }",
        "query Q { users(order: { name: DESC }) { id } }"
    )]
    [TestCase(
        "Variable type widened",
        "query Q($id: Int!) { user(id: $id) { id } }",
        "query Q($id: ID!) { user(id: $id) { id } }"
    )]
    [TestCase(
        "New optional variable added",
        "query Q($id: Int!) { user(id: $id) { id } }",
        "query Q($id: Int!, $verbose: Boolean) { user(id: $id) { id } }"
    )]
    [TestCase(
        "Default variable value changed",
        "query Q($n: Int = 10) { items(first: $n) { id } }",
        "query Q($n: Int = 25) { items(first: $n) { id } }"
    )]
    [TestCase(
        "Nested arg only",
        "query Q { user(id: 1) { posts(first: 5) { id } } }",
        "query Q { user(id: 2) { posts(first: 50) { id } } }"
    )]
    [TestCase(
        "Comment-only differences",
        "query Q { user { id } }",
        "# updated 2026-05\nquery Q { user { id } }"
    )]
    public void Pairs_SameShape_HashEqually(string label, string a, string b)
    {
        Fp(a).Should().Be(Fp(b), $"{label}: documents should have identical shape");
    }

    // ---------- DIFFERENT SHAPE PAIRS ----------

    [TestCase("Add a field", "query Q { user { id } }", "query Q { user { id name } }")]
    [TestCase("Remove a field", "query Q { user { id name } }", "query Q { user { id } }")]
    [TestCase("Rename a field", "query Q { user { name } }", "query Q { user { fullName } }")]
    [TestCase("Add an alias", "query Q { user { id } }", "query Q { user { uid: id } }")]
    [TestCase("Change an alias", "query Q { user { uid: id } }", "query Q { user { userId: id } }")]
    [TestCase(
        "Add nested field",
        "query Q { user { posts { id } } }",
        "query Q { user { posts { id title } } }"
    )]
    [TestCase(
        "Remove nested field",
        "query Q { user { posts { id title } } }",
        "query Q { user { posts { id } } }"
    )]
    [TestCase(
        "Wrap field in inline fragment on different type",
        "query Q { search { id } }",
        "query Q { search { ... on User { id } } }"
    )]
    [TestCase(
        "Inline fragment branch added",
        "query Q { search { ... on User { id } } }",
        "query Q { search { ... on User { id } ... on Campaign { title } } }"
    )]
    [TestCase(
        "Add @include to a field",
        "query Q { user { id name } }",
        "query Q($f: Boolean!) { user { id name @include(if: $f) } }"
    )]
    [TestCase(
        "Add @skip to a field",
        "query Q { user { id name } }",
        "query Q($f: Boolean!) { user { id @skip(if: $f) name } }"
    )]
    [TestCase(
        "Fragment definition changed (drops field)",
        "query Q { user { ...UC } } fragment UC on User { id name email }",
        "query Q { user { ...UC } } fragment UC on User { id name }"
    )]
    [TestCase(
        "Fragment vs inlined-different",
        "query Q { user { ...UC } } fragment UC on User { id email }",
        "query Q { user { id name } }"
    )]
    [TestCase("Adding __typename", "query Q { user { id } }", "query Q { user { id __typename } }")]
    [TestCase("Top-level field swap", "query Q { user { id } }", "query Q { admin { id } }")]
    [TestCase(
        "Two-level rename",
        "query Q { user { posts { id } } }",
        "query Q { user { articles { id } } }"
    )]
    [TestCase(
        "Add unique field via spread inside union",
        "query Q { result { ... on A { x } } }",
        "query Q { result { ... on A { x } ... on B { y } } }"
    )]
    [TestCase(
        "Aliased duplicate",
        "query Q { user { id } }",
        "query Q { user { id idAgain: id } }"
    )]
    [TestCase("Mutation vs query", "query Q { user { id } }", "mutation Q { user { id } }")]
    [TestCase("Subscription vs query", "query Q { user { id } }", "subscription Q { user { id } }")]
    // Fragment spreads carry their type condition into the canonical hash. Without
    // schema info we can't tell whether a `... on User` is a no-op narrowing or a
    // real union/interface branch, so we err on the side of "different shape".
    // Operators rewriting a fragment-spread into inlined fields will see the
    // guardrail flag the change; passing --force is the documented escape hatch.
    [TestCase(
        "Fragment spread vs inlined fields",
        "query Q { user { ...UserCard } } fragment UserCard on User { id name }",
        "query Q { user { id name } }"
    )]
    public void Pairs_DifferentShape_HashDifferently(string label, string a, string b)
    {
        Fp(a).Should().NotBe(Fp(b), $"{label}: documents should hash to different shapes");
    }

    // ---------- DETERMINISM + EDGE CASES ----------

    [Test]
    public void Compute_SameDocumentTwice_ProducesIdenticalHash()
    {
        const string doc = "query Q { user(id: 1) { id name } }";
        Fp(doc).Should().Be(Fp(doc));
    }

    [Test]
    public void Compute_HashIs64HexChars()
    {
        var hash = Fp("query Q { user { id } }");
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Test]
    public void Compute_OperationName_DisambiguatesMultipleOps()
    {
        const string doc = """
            query A { user { id } }
            query B { admin { id name } }
            """;

        var aHash = Fp(doc, "A");
        var bHash = Fp(doc, "B");

        aHash.Should().NotBe(bHash);
    }

    [Test]
    public void Compute_MultipleOperations_NoNameSpecified_Throws()
    {
        const string doc = """
            query A { user { id } }
            query B { admin { id } }
            """;

        Action act = () => Fp(doc);
        act.Should().Throw<InvalidOperationException>().WithMessage("*multiple operations*");
    }

    [Test]
    public void Compute_NamedOperationMissing_Throws()
    {
        Action act = () => Fp("query A { user { id } }", "DoesNotExist");
        act.Should().Throw<InvalidOperationException>().WithMessage("*operation*DoesNotExist*");
    }

    [Test]
    public void Compute_FragmentOnlyDocument_Throws()
    {
        Action act = () => Fp("fragment F on User { id }");
        act.Should().Throw<InvalidOperationException>().WithMessage("*no executable operation*");
    }

    [Test]
    public void Compute_RecursiveFragmentCycle_DoesNotInfiniteLoop()
    {
        // Fragment A spreads B which spreads A. The recursion guard should
        // break the cycle and produce a finite hash.
        const string doc = """
            query Q { user { ...A } }
            fragment A on User { id ...B }
            fragment B on User { name ...A }
            """;

        var hash = Fp(doc);
        hash.Should().HaveLength(64);
    }

    [Test]
    public void Compute_MissingFragment_DoesNotThrow()
    {
        // Missing fragment: the parser allows it; the walker should skip,
        // not crash. The shape will differ from the all-defined version.
        const string doc = "query Q { user { ...UndefinedFragment } }";
        var hash = Fp(doc);
        hash.Should().HaveLength(64);
    }

    [Test]
    public void Compute_DeeplyNestedSelection_HashesDeterministically()
    {
        const string doc = """
            query Q {
                a {
                    b {
                        c {
                            d {
                                e { id }
                            }
                        }
                    }
                }
            }
            """;

        Fp(doc).Should().Be(Fp(doc));
    }

    [Test]
    public void Compute_NullOrEmptyDocument_Throws()
    {
        Action emptyAct = () => Fp(string.Empty);
        emptyAct.Should().Throw<ArgumentException>();

        Action nullAct = () => Fp(null!);
        nullAct.Should().Throw<ArgumentException>();
    }
}
