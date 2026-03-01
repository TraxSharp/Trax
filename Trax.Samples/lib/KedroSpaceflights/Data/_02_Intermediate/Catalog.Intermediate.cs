using Flowthru.Data;
using KedroSpaceflights.Data._02_Intermediate.Schemas;

namespace KedroSpaceflights.Data;

public partial class Catalog
{
    /// <summary>
    /// Preprocessed company data with validated and strongly-typed fields.
    /// </summary>
    public ICatalogEntry<IEnumerable<PreprocessedCompanySchema>> PreprocessedCompanies =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Parquet<PreprocessedCompanySchema>(
                label: "PreprocessedCompanies",
                filePath: $"{_basePath}/_02_Intermediate/Datasets/preprocessed_companies.parquet"
            )
        );

    /// <summary>
    /// Preprocessed shuttle data with validated and strongly-typed fields.
    /// </summary>
    public ICatalogEntry<IEnumerable<PreprocessedShuttleSchema>> PreprocessedShuttles =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Parquet<PreprocessedShuttleSchema>(
                label: "PreprocessedShuttles",
                filePath: $"{_basePath}/_02_Intermediate/Datasets/preprocessed_shuttles.parquet"
            )
        );
}
