namespace Trax.Effect.Tests.Meta.Tests;

[TestFixture]
public class ModelPersistentPairingTests
{
    private const string ModelsDir = "src/Trax.Effect/Models";
    private const string PersistentDir = "src/Trax.Effect.Data/Models";

    /// <summary>
    /// Models that legitimately do NOT have a Persistent counterpart. Each entry must justify why.
    /// </summary>
    private static readonly HashSet<string> ModelsWithoutPersistent = new(StringComparer.Ordinal)
    {
        // Host: TraxHostInfo is auto-detected at startup and stamped onto Metadata records.
        //       It is not persisted as a standalone entity.
        "Host",
        // JunctionMetadata: stored as JSON inside the Metadata 'junctions' column;
        //       no top-level table or DbSet of its own.
        "JunctionMetadata",
    };

    [Test]
    public void Every_Persistent_HasMatching_Model()
    {
        var persistentDirAbs = RepoRoot.Combine(PersistentDir);
        var modelDirAbs = RepoRoot.Combine(ModelsDir);

        Directory.Exists(persistentDirAbs).Should().BeTrue($"missing '{PersistentDir}'.");
        Directory.Exists(modelDirAbs).Should().BeTrue($"missing '{ModelsDir}'.");

        var orphans = new List<string>();

        foreach (var persistentSubdir in Directory.EnumerateDirectories(persistentDirAbs))
        {
            var entityName = Path.GetFileName(persistentSubdir);
            var persistentFile = Path.Combine(persistentSubdir, $"Persistent{entityName}.cs");
            if (!File.Exists(persistentFile))
                continue; // skip directories that don't contain a Persistent file (e.g. shared helpers)

            var modelDir = Path.Combine(modelDirAbs, entityName);
            var modelFile = Path.Combine(modelDir, $"{entityName}.cs");

            if (!Directory.Exists(modelDir) || !File.Exists(modelFile))
            {
                orphans.Add(
                    $"{RepoRoot.Relative(persistentFile)} -> expected matching model at "
                        + $"{ModelsDir}/{entityName}/{entityName}.cs"
                );
            }
        }

        orphans
            .Should()
            .BeEmpty(
                "Every Persistent<Entity>.cs in Trax.Effect.Data/Models/<Entity>/ must have a matching "
                    + "<Entity>.cs in Trax.Effect/Models/<Entity>/. CLAUDE.md > Pattern Matching > Data "
                    + "models requires this layout. Orphans:\n  "
                    + string.Join("\n  ", orphans)
            );
    }

    [Test]
    public void Every_Model_HasMatching_Persistent_Or_IsExempt()
    {
        var persistentDirAbs = RepoRoot.Combine(PersistentDir);
        var modelDirAbs = RepoRoot.Combine(ModelsDir);

        Directory.Exists(persistentDirAbs).Should().BeTrue($"missing '{PersistentDir}'.");
        Directory.Exists(modelDirAbs).Should().BeTrue($"missing '{ModelsDir}'.");

        var orphans = new List<string>();

        foreach (var modelSubdir in Directory.EnumerateDirectories(modelDirAbs))
        {
            var entityName = Path.GetFileName(modelSubdir);
            if (ModelsWithoutPersistent.Contains(entityName))
                continue;

            var modelFile = Path.Combine(modelSubdir, $"{entityName}.cs");
            if (!File.Exists(modelFile))
                continue; // not a canonical model directory

            var persistentFile = Path.Combine(
                persistentDirAbs,
                entityName,
                $"Persistent{entityName}.cs"
            );

            if (!File.Exists(persistentFile))
            {
                orphans.Add(
                    $"{RepoRoot.Relative(modelFile)} -> expected matching persistent at "
                        + $"{PersistentDir}/{entityName}/Persistent{entityName}.cs"
                );
            }
        }

        orphans
            .Should()
            .BeEmpty(
                "Every <Entity>.cs in Trax.Effect/Models/<Entity>/ must have a matching "
                    + "Persistent<Entity>.cs in Trax.Effect.Data/Models/<Entity>/, OR be added to "
                    + "ModelsWithoutPersistent with a justification. Orphans:\n  "
                    + string.Join("\n  ", orphans)
            );
    }
}
