using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using Trax.Core.Decisions;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions;

/// <summary>
/// The state hash a replay compares covers every field of the state's runtime type, so two states
/// a decider could tell apart never hash alike, and it gives no hash, rather than a wrong one or an
/// exception, for a state it cannot read the same way every time. Pins core/0004
/// (docs/adr/0004-a-recorded-answer-replays-only-into-the-same-state.md).
/// </summary>
[Property("adr", "docs/adr/0004-a-recorded-answer-replays-only-into-the-same-state.md")]
public class StateDigestTests : TestSetup
{
    private const string Adr = "0004-a-recorded-answer-replays-only-into-the-same-state.md";

    [TestCaseSource(nameof(Shapes))]
    public void Of_StatesThatDifferOnlyInAmount_HashDifferently(Func<decimal, object> make)
    {
        StateDigest
            .Of(make(20))
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(make(2000)),
                $"{Adr}: a decider can read the amount, so the hash must cover it"
            );
    }

    [TestCaseSource(nameof(Shapes))]
    public void Of_EqualStatesBuiltSeparately_HashAlike(Func<decimal, object> make)
    {
        StateDigest
            .Of(make(20))
            .Should()
            .NotBeNull()
            .And.Be(
                StateDigest.Of(make(20)),
                $"{Adr}: the same state must replay, however many times it is built"
            );
    }

    /// <summary>Each shape a JSON hash would have written the same for 20 and 2000.</summary>
    public static IEnumerable<TestCaseData> Shapes()
    {
        yield return Shape("ATuple", a => (new Order(a), new Customer("ann")));
        yield return Shape("APublicField", a => new FieldOrder { Amount = a });
        yield return Shape(
            "ADerivedTypeHeldAsItsAbstractBase",
            a => new Checkout { Payment = new Card { Amount = a } }
        );
        yield return Shape(
            "ADerivedTypeHeldAsAnInterface",
            a => new Wallet { Payment = new CardPayment(a) }
        );
        yield return Shape("AJsonIgnoredProperty", a => new IgnoredOrder { Amount = a });
        yield return Shape("APrivateField", a => new PrivateOrder(a));
        yield return Shape("AnAnonymousType", a => new { Amount = a });
        yield return Shape("AList", a => new List<Order> { new(1), new(a) });
        yield return Shape(
            "ADictionary",
            a => new Dictionary<string, decimal> { ["one"] = 1, ["amount"] = a }
        );
    }

    private static TestCaseData Shape(string name, Func<decimal, object> make) =>
        new TestCaseData(make).SetArgDisplayNames(name);

    [Test]
    public void Of_ADifferentRuntimeTypeWithTheSameFields_HashesDifferently()
    {
        StateDigest
            .Of(new Checkout { Payment = new Card { Amount = 20 } })
            .Should()
            .NotBe(StateDigest.Of(new Checkout { Payment = new Voucher { Amount = 20 } }), Adr);
    }

    [Test]
    public void Of_AListWhoseCapacityDiffers_HashesAlike()
    {
        var grown = new List<int>(100) { 1, 2 };
        grown.Add(3);
        grown.RemoveAt(2);

        StateDigest
            .Of(grown)
            .Should()
            .Be(
                StateDigest.Of(new List<int> { 1, 2 }),
                $"{Adr}: a framework collection is its elements, not its capacity or version"
            );
    }

    [Test]
    public void Of_ADictionaryThatComparesKeysAnotherWay_HashesDifferently()
    {
        StateDigest
            .Of(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1 })
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 }),
                $"{Adr}: a lookup answers differently under another comparer"
            );
    }

    [Test]
    public void Of_ACycle_GivesNoHash()
    {
        var node = new Node();
        node.Next = node;

        StateDigest.Of(node).Should().BeNull($"{Adr}: a cycle cannot be written in full");
    }

    [Test]
    public void Of_AnObjectSharedTwiceWithoutACycle_Hashes()
    {
        var shared = new Order(20);

        StateDigest.Of((shared, shared)).Should().NotBeNull(Adr);
    }

    [Test]
    public void Of_ALazyLoadingProxyHoldingADelegate_GivesNoHash()
    {
        StateDigest
            .Of(new ProxyOrder { Amount = 20 })
            .Should()
            .BeNull($"{Adr}: what a delegate would load cannot be hashed");
    }

    [Test]
    public void Of_NestingDeeperThanTheLimit_GivesNoHash()
    {
        var head = new Node();
        var at = head;

        for (var i = 0; i < StateDigest.MaxDepth + 5; i++)
            at = at.Next = new Node();

        StateDigest.Of(head).Should().BeNull(Adr);
    }

    [Test]
    public void Of_AFieldWhoseValueCannotBeRead_GivesNoHash()
    {
        StateDigest.Of(new HoldsAType { Kind = typeof(string) }).Should().BeNull(Adr);
        StateDigest.Of(new HoldsAStream()).Should().BeNull(Adr);
    }

    [Test]
    public void Of_IsAHashOfTheState_NotTheState()
    {
        StateDigest.Of(new Customer("refund 2000")).Should().MatchRegex("^s1:[0-9a-f]{64}$", Adr);
    }

    private static readonly StateHashKey TestKey = new([
        .. Enumerable.Range(0, 32).Select(i => (byte)i),
    ]);

    /// <summary>
    /// Pins the encoding: a change to it changes these, and every hash a host has recorded with
    /// them. The string-keyed sets and dictionaries are written in an order this process chose at
    /// random, so matching them here also pins that the hash does not depend on it.
    /// </summary>
    [TestCaseSource(nameof(Golden))]
    public void Of_PinnedStates_HashToTheirPinnedValues(object? state, string unkeyed, string keyed)
    {
        StateDigest.Of(state).Should().Be(unkeyed, Adr);
        StateDigest.Of(state, TestKey).Should().Be(keyed, Adr);
    }

    public static IEnumerable<TestCaseData> Golden()
    {
        yield return Pinned(
            "Null",
            null,
            "s1:6e340b9cffb37a989ca544e6bb780a2c78901d3fb33738768511a30617afa01d",
            "k1:e711546e3faad4c7c4aa756bc26cad6abea8241984a0f6b0839c70ca61c4ef88"
        );
        yield return Pinned(
            "AnInt",
            42,
            "s1:b2a2626f1ebcb7d4937d16ee697c025193af4aa5fad7ce07049152d0ebaf8d88",
            "k1:b4a9200aabee5b449912d34d058a6d73b50fa798dbc8e28ea9ceb5f69dfbe833"
        );
        yield return Pinned(
            "AString",
            "refund",
            "s1:b0543f011252a42dfa0606788cf294f3b40cb0700ae712c2c2bd97eb92ba714d",
            "k1:e174679cf0b6d3f0d85f0b71055fe5834c11b5859a4daa1473e6a9e5c91275aa"
        );
        yield return Pinned(
            "ATuple",
            (1, "a", 2.5m),
            "s1:b91a5421fe1cdfabf3898f433617fa541170a05dfc155f5f9102bf0c1eb32082",
            "k1:d4f25ef7a7f3e35908c85408467fb06257ed42723681cb7334d7e3d2b1a2097c"
        );
        yield return Pinned(
            "AList",
            new List<int> { 1, 2, 3 },
            "s1:b61f084dadaeb548bc0152185ade5113b366a40f60b7fd385fc0a7783d0a508d",
            "k1:b2b6d565568c7f530a03bb5433e15e377f602898b95776064c833428a850e7bf"
        );
        yield return Pinned(
            "ADictionary",
            new Dictionary<string, int>
            {
                ["a"] = 1,
                ["b"] = 2,
                ["c"] = 3,
            },
            "s1:4120c1e843459babb3cb5758c2d7268e308f9ff3474c4da12647e0b4f585eb18",
            "k1:75934e5313e5cc9b6dcdaf253306aa66a3e08d5faf95a9dc0eefa1ce9bfdf895"
        );
        yield return Pinned(
            "AnImmutableHashSet",
            ImmutableHashSet.Create(StringComparer.Ordinal, "x", "y", "z", "w"),
            "s1:53ff01d3688aa7646245446244d1b194b817ec1a449c2bc2812e0df06b1a0079",
            "k1:aa335fcdcc1677e8071c26de6cc929bf74670e918e909a981d714239f868c131"
        );
        yield return Pinned(
            "Leaves",
            (
                new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
                new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(2)),
                1.5d,
                DayOfWeek.Friday,
                Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                new Uri("https://example.com/a"),
                CultureInfo.InvariantCulture,
                (Int128)1 << 64
            ),
            "s1:f2485a1565a9807206e30b584c5d2b7709f7ea56075bc9f97bbb40a5b56d08c4",
            "k1:8f74fa90608f3a95dbaa2291b0d05b18eb360c042af18b583ff25c9c2fdc348f"
        );
    }

    private static TestCaseData Pinned(string name, object? state, string unkeyed, string keyed) =>
        new TestCaseData(state, unkeyed, keyed).SetArgDisplayNames(name);

    [Test]
    public void Of_KeyedAndUnkeyed_NeverMatch()
    {
        var state = new Order(20);

        StateDigest.Of(state).Should().MatchRegex("^s1:[0-9a-f]{64}$");
        StateDigest.Of(state, TestKey).Should().MatchRegex("^k1:[0-9a-f]{64}$");
        StateDigest
            .Of(state, TestKey)
            .Should()
            .NotBe(StateDigest.Of(state, new StateHashKey(new byte[32])), Adr);
        StateDigest.Of(state, TestKey).Should().Be(StateDigest.Of(new Order(20), TestKey));
    }

    [Test]
    public void StateHashKey_RefusesAShortOrMissingKey_AndCopiesIt()
    {
        FluentActions.Invoking(() => new StateHashKey(null!)).Should().Throw<ArgumentException>();
        FluentActions
            .Invoking(() => new StateHashKey(new byte[31]))
            .Should()
            .Throw<ArgumentException>();

        var bytes = new byte[32];
        var key = new StateHashKey(bytes);
        var before = StateDigest.Of(1, key);
        bytes[0] = 1;

        StateDigest.Of(1, key).Should().Be(before, "the key holds its own copy");
    }

    [Test]
    public void Of_AsManyValuesAsTheCapAllows_Hashes_AndOneMoreGivesNoHash()
    {
        // The list itself is one value, and each element another.
        StateDigest
            .Of(new List<int>(Enumerable.Range(0, StateDigest.MaxValues - 1)))
            .Should()
            .NotBeNull("the documented value cap must be reachable within the byte cap");
        StateDigest
            .Of(new List<int>(Enumerable.Range(0, StateDigest.MaxValues)))
            .Should()
            .BeNull(Adr);
    }

    [Test]
    public void Of_MoreBytesThanTheCap_GivesNoHash()
    {
        StateDigest
            .Of(new string('x', (int)(StateDigest.MaxBytes / 2) - 1024))
            .Should()
            .NotBeNull();
        StateDigest.Of(new string('x', (int)(StateDigest.MaxBytes / 2))).Should().BeNull(Adr);
    }

    [Test]
    public void Of_ASharedGraphThatWouldBeWalkedExponentiallyOften_GivesNoHashQuickly()
    {
        var node = new Fork();

        for (var i = 0; i < 50; i++)
            node = new Fork { Left = node, Right = node };

        var watch = System.Diagnostics.Stopwatch.StartNew();

        StateDigest.Of(node).Should().BeNull($"{Adr}: the value cap bounds the walk");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void Of_AnInlineArray_CoversEveryElement()
    {
        var first = new Four();
        var second = new Four();
        first[3] = 1;
        second[3] = 2;

        StateDigest.Of(first).Should().NotBeNull().And.NotBe(StateDigest.Of(second), Adr);
        StateDigest
            .Of(new HoldsFour { Values = first })
            .Should()
            .NotBe(StateDigest.Of(new HoldsFour { Values = second }));
    }

    [Test]
    public unsafe void Of_AFixedBuffer_CoversEveryElement()
    {
        var first = new Fixed();
        var second = new Fixed();
        first.Values[3] = 1;
        second.Values[3] = 2;

        StateDigest.Of(first).Should().NotBeNull().And.NotBe(StateDigest.Of(second), Adr);
        second.Values[3] = 1;
        StateDigest.Of(first).Should().Be(StateDigest.Of(second));
    }

    [Test]
    public void Of_ImmutableDictionariesThatCompareKeysAnotherWay_HashDifferently()
    {
        StateDigest
            .Of(ImmutableDictionary.Create<string, int>(StringComparer.Ordinal).Add("a", 1))
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(
                    ImmutableDictionary
                        .Create<string, int>(StringComparer.OrdinalIgnoreCase)
                        .Add("a", 1)
                ),
                Adr
            );
        StateDigest
            .Of(ImmutableHashSet.Create(StringComparer.Ordinal, "a"))
            .Should()
            .NotBe(StateDigest.Of(ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "a")));
        StateDigest
            .Of(new SortedSet<string>(StringComparer.Ordinal) { "a" })
            .Should()
            .NotBe(StateDigest.Of(new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "a" }));
        StateDigest
            .Of(new Dictionary<string, int>(StringComparer.InvariantCulture) { ["a"] = 1 })
            .Should()
            .NotBeNull("a culture comparer is written by its culture")
            .And.NotBe(
                StateDigest.Of(
                    new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase)
                    {
                        ["a"] = 1,
                    }
                )
            );
    }

    [Test]
    public void Of_AReadOnlyWrapper_IsWrittenAsWhatItWraps()
    {
        ReadOnlyDictionary<string, int> Wrap(StringComparer comparer)
        {
            var wrapped = new ReadOnlyDictionary<string, int>(
                new Dictionary<string, int>(comparer) { ["a"] = 1 }
            );
            _ = wrapped.Keys; // fills a cache the wrapper keeps
            return wrapped;
        }

        StateDigest
            .Of(Wrap(StringComparer.Ordinal))
            .Should()
            .NotBeNull()
            .And.Be(
                StateDigest.Of(
                    new ReadOnlyDictionary<string, int>(
                        new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1 }
                    )
                )
            )
            .And.NotBe(StateDigest.Of(Wrap(StringComparer.OrdinalIgnoreCase)), Adr);
        StateDigest
            .Of(new List<int> { 1, 2 }.AsReadOnly())
            .Should()
            .NotBe(StateDigest.Of(new List<int> { 1, 3 }.AsReadOnly()));
    }

    [Test]
    public void Of_ANonGenericHashtable_GivesNoHash()
    {
        StateDigest.Of(new Hashtable { ["a"] = 1 }).Should().BeNull(Adr);
    }

    [Test]
    public void Of_AUriSubclass_IsWrittenByItsFields()
    {
        StateDigest
            .Of(new TaggedUri("https://example.com/", "one"))
            .Should()
            .NotBe(StateDigest.Of(new TaggedUri("https://example.com/", "two")), Adr)
            .And.NotBe(StateDigest.Of(new Uri("https://example.com/")));
    }

    [Test]
    public void Of_ArraysThatDifferOnlyInLowerBounds_HashDifferently()
    {
        var zero = Array.CreateInstance(typeof(int), [2, 2], [0, 0]);
        var one = Array.CreateInstance(typeof(int), [2, 2], [1, 0]);

        StateDigest.Of(zero).Should().NotBeNull().And.NotBe(StateDigest.Of(one), Adr);
        StateDigest
            .Of(new int[2, 3])
            .Should()
            .NotBe(StateDigest.Of(new int[3, 2]), "each dimension's length counts");
    }

    [Test]
    public void Of_UnorderedCollectionsFilledInAnotherOrder_HashAlike()
    {
        StateDigest
            .Of(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 })
            .Should()
            .Be(StateDigest.Of(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 }), Adr);
        StateDigest
            .Of(new HashSet<object> { new Order(1), new Customer("ann") })
            .Should()
            .Be(
                StateDigest.Of(new HashSet<object> { new Customer("ann"), new Order(1) }),
                "an element's encoding does not depend on which types its siblings wrote first"
            );
        StateDigest
            .Of(ImmutableHashSet.CreateRange(Enumerable.Range(0, 100).Select(i => $"k{i}")))
            .Should()
            .Be(
                StateDigest.Of(
                    ImmutableHashSet.CreateRange(
                        Enumerable.Range(0, 100).Reverse().Select(i => $"k{i}")
                    )
                )
            );
        StateDigest
            .Of(new List<int> { 1, 2 })
            .Should()
            .NotBe(StateDigest.Of(new List<int> { 2, 1 }), "a list's order is part of it");
    }

    [Test]
    public void Of_ANonGenericSortedList_GivesNoHash()
    {
        StateDigest
            .Of(new SortedList(StringComparer.OrdinalIgnoreCase) { ["a"] = 1 })
            .Should()
            .BeNull($"{Adr}: its comparer cannot be read without running it");
        StateDigest.Of(new SortedList { ["a"] = 1 }).Should().BeNull(Adr);
    }

    [Test]
    public void Of_ABlockingCollection_GivesNoHash_AndNeverRunsWhatItHolds()
    {
        var open = new BlockingCollection<int> { 1 };
        var completed = new BlockingCollection<int> { 1 };
        completed.CompleteAdding();
        var held = new WatchedBag();

        StateDigest
            .Of(open)
            .Should()
            .BeNull($"{Adr}: whether adding is complete is not an element");
        StateDigest.Of(completed).Should().BeNull(Adr);
        StateDigest.Of(new BlockingCollection<int>(held)).Should().BeNull(Adr);
        held.Touched.Should().BeFalse("the hash never runs the state's own code");
    }

    [Test]
    public void Of_ACollectionOverACustomList_IsWrittenByTheListsFields_AndNeverEnumeratesIt()
    {
        var yes = new WatchedList(flag: true);
        var no = new WatchedList(flag: false);

        StateDigest
            .Of(new Collection<int>(yes))
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(new Collection<int>(no)),
                $"{Adr}: what the wrapped list holds counts, not only what it enumerates"
            );
        yes.Touched.Should().BeFalse("the hash never runs the state's own code");
        no.Touched.Should().BeFalse("the hash never runs the state's own code");
        StateDigest
            .Of(new Collection<int>(new List<int> { 1, 2 }))
            .Should()
            .NotBe(StateDigest.Of(new Collection<int>(new List<int> { 1, 3 })));
    }

    [Test]
    public void Of_ADictionarysKeysUnderAnotherComparer_HashDifferently()
    {
        Dictionary<string, int> Dictionary(StringComparer comparer) => new(comparer) { ["a"] = 1 };
        SortedDictionary<string, int> Sorted(StringComparer comparer) =>
            new(comparer) { ["a"] = 1 };
        SortedList<string, int> Listed(StringComparer comparer) => new(comparer) { ["a"] = 1 };

        StateDigest
            .Of(Dictionary(StringComparer.Ordinal).Keys)
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(Dictionary(StringComparer.OrdinalIgnoreCase).Keys),
                $"{Adr}: a key view answers Contains by its dictionary's comparer"
            )
            .And.Be(StateDigest.Of(Dictionary(StringComparer.Ordinal).Keys));
        StateDigest
            .Of(Sorted(StringComparer.Ordinal).Keys)
            .Should()
            .NotBeNull()
            .And.NotBe(StateDigest.Of(Sorted(StringComparer.OrdinalIgnoreCase).Keys), Adr);
        StateDigest
            .Of(Listed(StringComparer.Ordinal).Keys)
            .Should()
            .NotBeNull()
            .And.NotBe(StateDigest.Of(Listed(StringComparer.OrdinalIgnoreCase).Keys), Adr);
    }

    [Test]
    public void Of_AnArrayListWrapper_GivesNoHash_AndNeverRunsWhatItWraps()
    {
        var wrapped = new WatchedNonGenericList();

        StateDigest.Of(ArrayList.Adapter(wrapped)).Should().BeNull(Adr);
        wrapped.Touched.Should().BeFalse("the hash never runs the state's own code");
        StateDigest.Of(ArrayList.ReadOnly(new ArrayList { 1 })).Should().BeNull(Adr);
        StateDigest
            .Of(new ArrayList { 1 })
            .Should()
            .NotBeNull("a plain ArrayList is written as its elements");
    }

    [Test]
    public void Of_ACollectionThatEnumeratesByItsOwnCode_GivesNoHash_AndNeverRunsIt()
    {
        var list = new ReEnumerated { 1 };

        StateDigest
            .Of(list)
            .Should()
            .BeNull($"{Adr}: the hash never runs the state's own code to read it");
        list.Enumerated.Should().BeFalse();
    }

    /// <summary>
    /// A keyed collection is written as the list it keeps, with its comparer, so two that look
    /// keys up another way hash differently.
    /// </summary>
    [Test]
    public void Of_KeyedCollectionsThatCompareKeysAnotherWay_HashDifferently()
    {
        StateDigest
            .Of(new Keyed(StringComparer.Ordinal) { "a" })
            .Should()
            .NotBeNull()
            .And.NotBe(StateDigest.Of(new Keyed(StringComparer.OrdinalIgnoreCase) { "a" }), Adr)
            .And.Be(StateDigest.Of(new Keyed(StringComparer.Ordinal) { "a" }));
    }

    [Test]
    public void Of_ACultureOtherThanTheSharedInstance_GivesNoHash()
    {
        var modified = new CultureInfo("en-US");
        modified.NumberFormat.NumberDecimalSeparator = ",";

        StateDigest
            .Of(modified)
            .Should()
            .BeNull($"{Adr}: a culture whose formats were changed formats values another way");
        StateDigest
            .Of(CultureInfo.ReadOnly(modified))
            .Should()
            .BeNull($"{Adr}: a read-only copy keeps the changed formats");
        StateDigest
            .Of(new CultureInfo("en-US", useUserOverride: true))
            .Should()
            .BeNull($"{Adr}: a culture with the user's overrides formats by the host's settings");
        StateDigest.Of(new CultureInfo("en-US", useUserOverride: false)).Should().BeNull(Adr);
        StateDigest
            .Of(CultureInfo.GetCultureInfo("en-US"))
            .Should()
            .NotBeNull("the shared instance cannot be changed")
            .And.Be(StateDigest.Of(CultureInfo.GetCultureInfo("en-US")))
            .And.NotBe(StateDigest.Of(CultureInfo.GetCultureInfo("fr-FR")));
        StateDigest.Of(CultureInfo.InvariantCulture).Should().NotBeNull();
    }

    /// <summary>
    /// Two builds of one enum, the same name in an assembly of the same name, whose member with
    /// the value 1 was renamed between them: the value means something else, so it hashes
    /// differently.
    /// </summary>
    [Test]
    public void Of_AnEnumWhoseMembersChanged_HashesDifferently()
    {
        static object Verdict(string one, Type underlying, object pending, object other)
        {
            var module = AssemblyBuilder
                .DefineDynamicAssembly(
                    new AssemblyName("StateDigestEnums"),
                    AssemblyBuilderAccess.RunAndCollect
                )
                .DefineDynamicModule("StateDigestEnums");
            var verdict = module.DefineEnum("Probe.Verdict", TypeAttributes.Public, underlying);
            verdict.DefineLiteral("Pending", pending);
            verdict.DefineLiteral(one, other);
            return Enum.ToObject(verdict.CreateType(), other);
        }

        var approved = Verdict("Approved", typeof(int), 0, 1);

        StateDigest
            .Of(approved)
            .Should()
            .NotBeNull()
            .And.Be(StateDigest.Of(Verdict("Approved", typeof(int), 0, 1)))
            .And.NotBe(
                StateDigest.Of(Verdict("Rejected", typeof(int), 0, 1)),
                $"{Adr}: an enum's members are part of its shape"
            )
            .And.NotBe(
                StateDigest.Of(Verdict("Approved", typeof(long), 0L, 1L)),
                "so is the type underneath it"
            );
    }

    /// <summary>
    /// In the hour a clock goes back, one local time names two instants; a local time converted
    /// from the second of them carries a flag that says so, which its ticks and kind do not.
    /// </summary>
    [Test]
    public void Of_LocalTimesThatDifferOnlyInTheirDaylightSavingFlag_HashDifferently()
    {
        var first = new DateTime(2026, 11, 1, 1, 30, 0, DateTimeKind.Local);
        var bits = Unsafe.As<DateTime, ulong>(ref first) | 0x4000_0000_0000_0000UL;
        var second = Unsafe.As<ulong, DateTime>(ref bits);

        second.Kind.Should().Be(DateTimeKind.Local);
        second.Ticks.Should().Be(first.Ticks);
        StateDigest
            .Of(first)
            .Should()
            .NotBeNull()
            .And.NotBe(StateDigest.Of(second), $"{Adr}: the two convert to different instants");
    }

    /// <summary>
    /// Each value written directly, against one that differs from it only in what that encoding
    /// must keep, so no encoding can become lossy unnoticed.
    /// </summary>
    [TestCaseSource(nameof(LeavesThatDiffer))]
    public void Of_LeafValuesThatDiffer_HashDifferently(object one, object other)
    {
        StateDigest.Of(one).Should().NotBeNull().And.NotBe(StateDigest.Of(other), Adr);
        StateDigest.Of(one).Should().Be(StateDigest.Of(one), "the same value hashes alike");
    }

    public static IEnumerable<TestCaseData> LeavesThatDiffer()
    {
        var ticks = new DateTime(2026, 10, 3, 12, 0, 0).Ticks;

        yield return Differ(
            "DateTimeKind",
            new DateTime(ticks, DateTimeKind.Utc),
            new DateTime(ticks, DateTimeKind.Local)
        );
        yield return Differ(
            "DateTimeTicks",
            new DateTime(ticks, DateTimeKind.Utc),
            new DateTime(ticks + 1, DateTimeKind.Utc)
        );
        yield return Differ(
            "DateTimeOffsetOffset",
            new DateTimeOffset(ticks, TimeSpan.Zero),
            new DateTimeOffset(ticks, TimeSpan.FromHours(1))
        );
        yield return Differ("DoubleFraction", 1.5d, 1.0d);
        yield return Differ("DoubleSign", 0.0d, -0.0d);
        yield return Differ("FloatFraction", 1.5f, 1.0f);
        yield return Differ("Half", (Half)1.5f, (Half)1.0f);
        yield return Differ("Decimal", 1.5m, 1.50m);
        yield return Differ("Int128High", (Int128)1 << 64, (Int128)2 << 64);
        yield return Differ("Int128Low", (Int128)1, (Int128)2);
        yield return Differ("UInt128High", (UInt128)1 << 64, (UInt128)2 << 64);
        yield return Differ("BigInteger", BigInteger.Pow(2, 100), BigInteger.Pow(2, 100) + 1);
        yield return Differ("ULong", ulong.MaxValue, ulong.MaxValue - 1);
        yield return Differ("TimeSpan", TimeSpan.FromTicks(1), TimeSpan.FromTicks(2));
        yield return Differ("DateOnly", new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 4));
        yield return Differ("TimeOnly", new TimeOnly(1, 0), new TimeOnly(1, 0, 1));
        yield return Differ(
            "Guid",
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002")
        );
        yield return Differ(
            "Uri",
            new Uri("https://example.com/a"),
            new Uri("https://example.com/b")
        );
        yield return Differ(
            "Culture",
            CultureInfo.GetCultureInfo("en-US"),
            CultureInfo.GetCultureInfo("fr-FR")
        );
        yield return Differ(
            "CompareInfo",
            CultureInfo.GetCultureInfo("en-US").CompareInfo,
            CultureInfo.GetCultureInfo("fr-FR").CompareInfo
        );
        yield return Differ("Char", 'a', 'b');
        yield return Differ("Bool", true, false);
        yield return Differ("String", "a", "b");
    }

    private static TestCaseData Differ(string name, object one, object other) =>
        new TestCaseData(one, other).SetArgDisplayNames(name);

    [Test]
    public void Of_ATypeDerivedFromAFrameworkCollection_CoversItsOwnFields()
    {
        StateDigest
            .Of(new Batch { 1, 2 })
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(new Batch(approved: true) { 1, 2 }),
                $"{Adr}: what a derived type adds to a collection is part of it"
            )
            .And.NotBe(StateDigest.Of(new Batch { 1, 3 }));
    }

    [Test]
    public void Of_AWrapperSubclass_CoversItsOwnFields()
    {
        StateDigest
            .Of(new TaggedView([1, 2], "one"))
            .Should()
            .NotBeNull()
            .And.NotBe(
                StateDigest.Of(new TaggedView([1, 2], "two")),
                $"{Adr}: what a derived type adds to a wrapper is part of it"
            )
            .And.NotBe(StateDigest.Of(new TaggedView([1, 3], "one")));
    }

    [Test]
    public void Identity_NamesTheAssemblyOfTheTypeAndOfItsArguments()
    {
        StateDigest
            .Identity(typeof(List<Order>))
            .Should()
            .Contain("System.Private.CoreLib")
            .And.Contain(typeof(Order).Assembly.GetName().Name!);
    }

    public sealed class Fork
    {
        public Fork? Left { get; set; }
        public Fork? Right { get; set; }
    }

    [InlineArray(4)]
    public struct Four
    {
        private int _element;
    }

    public sealed class HoldsFour
    {
        public Four Values;
    }

    public unsafe struct Fixed
    {
        public fixed int Values[4];
    }

    public sealed class TaggedUri(string uri, string tag) : Uri(uri)
    {
        public string Tag { get; } = tag;
    }

    public sealed record Order(decimal Amount);

    public sealed record Customer(string Name);

    public sealed class FieldOrder
    {
        public decimal Amount;
    }

    public abstract class Payment
    {
        public string Kind { get; set; } = "x";
    }

    public sealed class Card : Payment
    {
        public decimal Amount { get; set; }
    }

    public sealed class Voucher : Payment
    {
        public decimal Amount { get; set; }
    }

    public sealed class Checkout
    {
        public Payment Payment { get; set; } = null!;
    }

    public interface IPayment;

    public sealed record CardPayment(decimal Amount) : IPayment;

    public sealed class Wallet
    {
        public IPayment Payment { get; set; } = null!;
    }

    public sealed class IgnoredOrder
    {
        [JsonIgnore]
        public decimal Amount { get; set; }
    }

    public sealed class PrivateOrder(decimal amount)
    {
        private readonly decimal _amount = amount;

        public bool IsLarge() => _amount > 100;
    }

    public sealed class Node
    {
        public Node? Next { get; set; }
    }

    /// <summary>Shaped like an ORM's lazy-loading proxy: a value, and a loader for the rest.</summary>
    public sealed class ProxyOrder
    {
        public decimal Amount { get; set; }

        public Func<object, string, object?> LazyLoader { get; set; } = (_, _) => null;
    }

    public sealed class HoldsAType
    {
        public Type Kind { get; set; } = null!;
    }

    public sealed class HoldsAStream
    {
        public Stream Body { get; set; } = new MemoryStream();
    }

    public sealed class Keyed(IEqualityComparer<string> comparer)
        : KeyedCollection<string, string>(comparer)
    {
        protected override string GetKeyForItem(string item) => item;
    }

    public sealed class Batch(bool approved = false) : List<int>
    {
        public bool Approved { get; } = approved;
    }

    public sealed class TaggedView(IList<int> items, string tag) : ReadOnlyCollection<int>(items)
    {
        public string Tag { get; } = tag;
    }

    /// <summary>A list that hands out its elements through its own enumerator.</summary>
    public sealed class ReEnumerated : List<int>, IEnumerable<int>
    {
        public bool Enumerated { get; private set; }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            Enumerated = true;
            yield return 2;
        }

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<int>)this).GetEnumerator();
    }

    /// <summary>A list that notes any call made to it, and that answers by a private flag.</summary>
    public sealed class WatchedList(bool flag) : IList<int>
    {
        private readonly bool _flag = flag;

        public bool Touched { get; private set; }

        private int Touch()
        {
            Touched = true;
            return 1;
        }

        public IEnumerator<int> GetEnumerator()
        {
            yield return Touch();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int this[int index]
        {
            get => Touch();
            set => Touch();
        }

        public int Count => Touch();

        public bool IsReadOnly => Touch() == 1;

        public void Add(int item) => Touch();

        public void Clear() => Touch();

        public bool Contains(int item) => Touch() == 1 && _flag;

        public void CopyTo(int[] array, int arrayIndex) => array[arrayIndex] = Touch();

        public bool Remove(int item) => Touch() == 0;

        public int IndexOf(int item) => Touch() - 1;

        public void Insert(int index, int item) => Touch();

        public void RemoveAt(int index) => Touch();
    }

    /// <summary>A non-generic list that notes any call made to it.</summary>
    public sealed class WatchedNonGenericList : IList
    {
        public bool Touched { get; private set; }

        private int Touch()
        {
            Touched = true;
            return 1;
        }

        public IEnumerator GetEnumerator()
        {
            yield return Touch();
        }

        public object? this[int index]
        {
            get => Touch();
            set => Touch();
        }

        public int Count => Touch();

        public bool IsReadOnly => Touch() == 1;

        public bool IsFixedSize => Touch() == 1;

        public bool IsSynchronized => Touch() == 0;

        public object SyncRoot => this;

        public int Add(object? value) => Touch();

        public void Clear() => Touch();

        public bool Contains(object? value) => Touch() == 1;

        public void CopyTo(Array array, int index) => array.SetValue(Touch(), index);

        public int IndexOf(object? value) => Touch() - 1;

        public void Insert(int index, object? value) => Touch();

        public void Remove(object? value) => Touch();

        public void RemoveAt(int index) => Touch();
    }

    /// <summary>A producer-consumer collection that notes any read made of it.</summary>
    public sealed class WatchedBag : IProducerConsumerCollection<int>
    {
        private readonly List<int> _items = [1];

        public bool Touched { get; private set; }

        public int Count => _items.Count;

        public bool IsSynchronized => false;

        public object SyncRoot => this;

        public void CopyTo(int[] array, int index)
        {
            Touched = true;
            _items.CopyTo(array, index);
        }

        public void CopyTo(Array array, int index)
        {
            Touched = true;
            ((ICollection)_items).CopyTo(array, index);
        }

        public IEnumerator<int> GetEnumerator()
        {
            Touched = true;
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int[] ToArray()
        {
            Touched = true;
            return [.. _items];
        }

        public bool TryAdd(int item)
        {
            _items.Add(item);
            return true;
        }

        public bool TryTake(out int item)
        {
            item = 0;
            return false;
        }
    }
}
