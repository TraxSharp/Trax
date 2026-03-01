using Flowthru.Data;
using KedroSpaceflights.Data._03_Primary.Schemas;

namespace KedroSpaceflights.Data;

public partial class Catalog
{
    /// <summary>
    /// Unified model input table combining shuttle, company, and review data.
    /// </summary>
    public ICatalogEntry<IEnumerable<ModelInputTableSchema>> ModelInputTable =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Parquet<ModelInputTableSchema>(
                label: "ModelInputTable",
                filePath: $"{_basePath}/_03_Primary/Datasets/model_input_table.parquet"
            )
        );

    /// <summary>
    /// Training dataset split from the model input table. Transient (memory only).
    /// </summary>
    public ICatalogEntry<IEnumerable<TrainingData>> TrainSplit =>
        GetOrCreateEntry(() => CatalogEntries.Enumerable.Memory<TrainingData>(label: "XTrain"));

    /// <summary>
    /// Test dataset split from the model input table. Transient (memory only).
    /// </summary>
    public ICatalogEntry<IEnumerable<TestData>> TestSplit =>
        GetOrCreateEntry(() => CatalogEntries.Enumerable.Memory<TestData>(label: "XTest"));
}
