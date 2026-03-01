using Flowthru.Data;
using KedroSpaceflights.Data._01_Raw.Schemas;

namespace KedroSpaceflights.Data;

public partial class Catalog
{
    /// <summary>
    /// Raw company data imported from external sources.
    /// </summary>
    public ICatalogEntry<IEnumerable<CompanySchema>> Companies =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Csv<CompanySchema>(
                label: "Companies",
                filePath: $"{_basePath}/_01_Raw/Datasets/companies.csv"
            )
        );

    /// <summary>
    /// Raw review data imported from external sources.
    /// </summary>
    public ICatalogEntry<IEnumerable<ReviewSchema>> Reviews =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Csv<ReviewSchema>(
                label: "Reviews",
                filePath: $"{_basePath}/_01_Raw/Datasets/reviews.csv"
            )
        );

    /// <summary>
    /// Raw shuttle data imported from external sources.
    /// </summary>
    public ICatalogEntry<IEnumerable<ShuttleSchema>> Shuttles =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Excel<ShuttleSchema>(
                label: "Shuttles",
                filePath: $"{_basePath}/_01_Raw/Datasets/shuttles.xlsx",
                sheetName: "Sheet1"
            )
        );
}
