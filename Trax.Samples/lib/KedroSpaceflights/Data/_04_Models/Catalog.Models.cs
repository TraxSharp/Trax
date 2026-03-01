using Flowthru.Data;
using KedroSpaceflights.Data._04_Models.Schemas;

namespace KedroSpaceflights.Data;

public partial class Catalog
{
    /// <summary>
    /// Trained linear regression model with coefficients and feature names.
    /// </summary>
    public ICatalogEntry<LinearRegressionModel> Regressor =>
        GetOrCreateEntry(() =>
            CatalogEntries.Single.Json<LinearRegressionModel>(
                label: "Regressor",
                filePath: $"{_basePath}/_04_Models/Datasets/regressor.json"
            )
        );

    /// <summary>
    /// Evaluation metrics for the trained regression model.
    /// </summary>
    public ICatalogEntry<ModelMetrics> ModelMetrics =>
        GetOrCreateEntry(() =>
            CatalogEntries.Single.Json<ModelMetrics>(
                label: "ModelMetrics",
                filePath: $"{_basePath}/_04_Models/Datasets/model_metrics.json"
            )
        );

    /// <summary>
    /// Model predictions containing actual and predicted values from the test set.
    /// Used for generating confusion matrices and prediction accuracy visualizations.
    /// </summary>
    public ICatalogEntry<IEnumerable<ModelPredictions>> ModelPredictions =>
        GetOrCreateEntry(() =>
            CatalogEntries.Enumerable.Json<ModelPredictions>(
                label: "ModelPredictions",
                filePath: $"{_basePath}/_04_Models/Datasets/model_predictions.json"
            )
        );
}
