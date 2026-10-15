using System.Reflection;
using Trax.Effect.StateMachine;
using Trax.Samples.Recovery.Machines;

namespace Trax.Samples.Recovery.E2E.Utilities;

/// <summary>
/// The topic map's engine, and the artifacts <c>trax machine generate</c> writes for it into the page's
/// <c>src/topicMap</c> folder (the command is in the sample's README).
/// </summary>
internal static class TopicMapArtifacts
{
    /// <summary>The folder holding the committed IR, twin and differential corpus.</summary>
    public static string Folder { get; } =
        Path.Combine(
            SamplesFolder(),
            "samples",
            "Recovery",
            "Trax.Samples.Recovery.Client",
            "src",
            "topicMap"
        );

    /// <summary>The committed differential corpus, written by <c>--corpus-out</c>.</summary>
    public static string DifferentialCorpus => Path.Combine(Folder, "differential.json");

    /// <summary>
    /// The client manifest the page's generated client was written from (<c>src/trax/manifest.json</c>): every id the
    /// page sends. Written by <c>--trax-export-client</c>, then <c>trax client</c> generates the client beside it.
    /// </summary>
    public static string ClientManifest =>
        Path.Combine(Path.GetDirectoryName(Folder)!, "trax", "manifest.json");

    /// <summary>
    /// The topic map's engine, built from the machine's own declaration. The machine is sealed and keeps its
    /// engine to itself, so its <c>Configure</c> is run against a fresh builder, which is what the machine does
    /// the first time it is used.
    /// </summary>
    public static SnapshotMachine<TopicMapState, TopicMapTrigger> Engine()
    {
        var configure =
            typeof(TopicMapMachine).GetMethod(
                "Configure",
                BindingFlags.Instance | BindingFlags.NonPublic,
                [typeof(IMachineBuilder<TopicMapState, TopicMapTrigger>)]
            ) ?? throw new InvalidOperationException("TopicMapMachine declares no Configure.");
        var builder = new MachineBuilder<TopicMapState, TopicMapTrigger>();
        configure.Invoke(new TopicMapMachine(), [builder]);
        return builder.Build().Engine;
    }

    private static string SamplesFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            folder is not null && !File.Exists(Path.Combine(folder.FullName, "Trax.Samples.slnx"))
        )
            folder = folder.Parent;
        return folder?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find Trax.Samples.slnx above {AppContext.BaseDirectory}."
            );
    }
}
