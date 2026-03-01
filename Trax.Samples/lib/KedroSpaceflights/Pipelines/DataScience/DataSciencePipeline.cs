using Flowthru.Pipelines;
using KedroSpaceflights.Data;

namespace KedroSpaceflights.Pipelines.DataScience;

/// <summary>
/// Creates the data science pipeline that trains and evaluates a price prediction model.
/// </summary>
public static class DataSciencePipeline
{
    /// <summary>
    /// Configuration parameters for the data science pipeline.
    /// </summary>
    public record Params
    {
        /// <summary>
        /// Configuration options for data splitting and model training.
        /// </summary>
        public Nodes.SplitDataNode.ModelOptions ModelOptions { get; init; } = new();
    }

    /// <summary>
    /// Creates the data science pipeline.
    /// </summary>
    /// <param name="catalog">The data catalog containing input and output entries.</param>
    /// <param name="parameters">Configuration parameters for the pipeline.</param>
    /// <returns>A configured pipeline that produces a trained model and evaluation metrics.</returns>
    public static Pipeline Create(Catalog catalog, Params parameters)
    {
        return PipelineBuilder.CreatePipeline(pipeline =>
        {
            pipeline.AddNode(
                label: "SplitData",
                description: "Splits model input data into training and test sets.",
                transform: Nodes.SplitDataNode.Create(parameters.ModelOptions),
                input: catalog.ModelInputTable,
                output: (catalog.TrainSplit, catalog.TestSplit)
            );

            pipeline.AddNode(
                label: "TrainModel",
                description: "Trains a regression model to predict shuttle prices.",
                transform: Nodes.TrainModelNode.Create(),
                input: catalog.TrainSplit,
                output: catalog.Regressor
            );

            pipeline.AddNode(
                label: "EvaluateModel",
                description: "Evaluates the trained model on the test set and computes metrics and predictions.",
                transform: Nodes.EvaluateModelNode.Create(),
                input: (catalog.Regressor, catalog.TestSplit),
                output: (catalog.ModelMetrics, catalog.ModelPredictions)
            );
        });
    }
}
