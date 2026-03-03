---
layout: default
title: Train Discovery
parent: Architecture
nav_order: 3
---

# Train Discovery & Routing

This page covers the internal implementation of train discovery and routing. For the user-facing explanation of how to use the `TrainBus`, see [Mediator](../usage-guide/mediator.md).

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

        // Create mapping: InputType -> TrainType
        InputTypeToTrain = allTrainTypes.ToDictionary(
            trainType => ExtractInputType(trainType),
            trainType => trainType
        );
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

        // 4. Set up parent-child relationship if needed
        if (parentMetadata != null)
            SetParentId(train, parentMetadata.Id);

        // 5. Execute train using reflection
        return await InvokeTrainRun<TOut>(train, trainInput);
    }
}
```

## Key Constraints and Design Decisions

### Input Type Uniqueness

Each input type maps to exactly one train. This is enforced at startup by the `TrainRegistry`'s `ToDictionary` call—duplicate input types cause an exception. See [SDK Reference: AddServiceTrainBus]({{ site.baseurl }}{% link sdk-reference/configuration/add-effect-train-bus.md %}) for the full uniqueness rules and code examples.

### Train Discovery Rules

1. **Must be concrete classes** (not abstract)
2. **Must implement IServiceTrain<,>**
3. **Must have parameterless constructor or be registered in DI**
4. **Should implement a non-generic interface** for better DI integration
