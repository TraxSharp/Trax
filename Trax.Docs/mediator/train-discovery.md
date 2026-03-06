---
layout: default
title: Train Discovery
parent: Mediator
nav_order: 1
---

# Train Discovery & Routing

This page covers the internal implementation of train discovery and routing. For the user-facing explanation of how to use the `TrainBus`, see [Mediator overview]({{ site.baseurl }}{% link mediator.md %}).

## TrainRegistry

```csharp
public class TrainRegistry : ITrainRegistry
{
    public Dictionary<Type, Type> InputTypeToTrain { get; set; }

    public TrainRegistry(params Assembly[] assemblies)
    {
        // Scan assemblies for train implementations
        var trainType = typeof(IServiceTrain<,>);
        var allTrainTypes = ScanAssembliesForTrains(assemblies, trainType);

        // Create mapping: InputType -> TrainType (duplicates are silently skipped)
        InputTypeToTrain = [];
        foreach (var train in allTrainTypes)
            InputTypeToTrain.TryAdd(ExtractInputType(train), train);
    }
}
```

## TrainBus

```csharp
public class TrainBus : ITrainBus
{
    public async Task<TOut> RunAsync<TOut>(object trainInput, Metadata? parentMetadata = null)
    {
        // 1. Find train type by input type
        var inputType = trainInput.GetType();
        var trainType = _registryService.InputTypeToTrain[inputType];

        // 2. Resolve train from DI container
        var train = _serviceProvider.GetRequiredService(trainType);

        // 3. Inject internal train properties (framework-level only)
        _serviceProvider.InjectProperties(train);

        // 4. Execute train using reflection, passing metadata for parent-child linking
        return await InvokeTrainRun<TOut>(train, trainInput, parentMetadata);
    }
}
```

## Key Constraints and Design Decisions

### Input Type Uniqueness

Each input type maps to exactly one train. When duplicate input types are found, the first registration wins — subsequent duplicates are silently skipped via `TryAdd`. See [SDK Reference: AddMediator]({{ site.baseurl }}{% link sdk-reference/mediator-api/add-service-train-bus.md %}) for the full uniqueness rules and code examples.

### Train Discovery Rules

1. **Must be concrete classes** (not abstract)
2. **Must implement IServiceTrain<,>**
3. **Must have parameterless constructor or be registered in DI**
4. **Should implement a non-generic interface** for better DI integration
