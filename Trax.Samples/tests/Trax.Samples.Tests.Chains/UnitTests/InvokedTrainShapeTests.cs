using System.Reflection;
using Trax.Core.Monad;
using Trax.Core.Train;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Trains.Discover;
using Trax.Samples.Recovery.Trains.Ingest;
using Trax.Samples.Recovery.Trains.Topics;

namespace Trax.Samples.Tests.Chains.UnitTests;

/// <summary>
/// The Recovery trains a state machine is to run: the topic map, which a user's own machine
/// builds, and the ingest and discovery of index partitions, which machines owned by the system run.
/// A train a machine runs must be cancellable from another host, so every step is an
/// <see cref="EffectJunction{TIn,TOut}"/>; its output is stored in the machine's context, so no
/// part of it is <see cref="TraxSensitiveAttribute"/>; and no run of it is broadcast to every
/// subscriber.
/// </summary>
[TestFixture]
public class InvokedTrainShapeTests
{
    private static readonly Type[] InvokedTrains =
    [
        typeof(BuildTopicMapTrain),
        typeof(IngestPartitionTrain),
        typeof(DiscoverPartitionsTrain),
    ];

    private static IEnumerable<TestCaseData> EachTrain() =>
        InvokedTrains.Select(t => new TestCaseData(t).SetArgDisplayNames(t.Name));

    [TestCaseSource(nameof(EachTrain))]
    public void EveryStep_IsAnEffectJunction(Type train)
    {
        var junctions = train
            .Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsNested: false })
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.Single());
        var graph = GraphOf(train);

        graph.Refusals.Should().BeEmpty();
        foreach (var node in Flatten(graph.Nodes))
        {
            node.Kind.Should()
                .NotBe(ChainStepKind.IChain, $"{node.Id} is resolved at run time, unchecked");
            if (node.Kind != ChainStepKind.Chain)
                continue;

            junctions.Should().ContainKey(node.Junction!);
            IsEffectJunction(junctions[node.Junction!])
                .Should()
                .BeTrue($"{node.Id} must be an EffectJunction to be cancelled from another host");
        }
    }

    [TestCaseSource(nameof(EachTrain))]
    public void TheOutput_ReachesNoSensitiveMember(Type train)
    {
        var output = train.BaseType!.GetGenericArguments()[1];

        SensitiveMembers(output, []).Should().BeEmpty();
    }

    [TestCaseSource(nameof(EachTrain))]
    public void TheTrain_IsNotBroadcast(Type train) =>
        train.GetCustomAttribute<TraxBroadcastAttribute>().Should().BeNull();

    private static ChainGraph GraphOf(Type train)
    {
        var arguments = train.BaseType!.GetGenericArguments();
        var chain = (ChainRecorder)
            train
                .GetMethod(nameof(Train<,>.DeclaredChain), Type.EmptyTypes)!
                .Invoke(Activator.CreateInstance(train), null)!;
        return ChainGraph.From(chain, train, arguments[0], arguments[1]);
    }

    private static IEnumerable<ChainGraphNode> Flatten(IEnumerable<ChainGraphNode> nodes) =>
        nodes.SelectMany(n => new[] { n }.Concat(n.Tracks.SelectMany(t => Flatten(t.Nodes))));

    private static bool IsEffectJunction(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (
                current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(EffectJunction<,>)
            )
                return true;
        return false;
    }

    private static IEnumerable<string> SensitiveMembers(Type type, HashSet<Type> seen)
    {
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum || !seen.Add(type))
            yield break;

        foreach (var argument in type.IsGenericType ? type.GetGenericArguments() : [])
        foreach (var found in SensitiveMembers(argument, seen))
            yield return found;

        if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            yield break;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<TraxSensitiveAttribute>() is not null)
                yield return $"{type.Name}.{property.Name}";
            foreach (var found in SensitiveMembers(property.PropertyType, seen))
                yield return found;
        }
    }
}
