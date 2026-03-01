---
layout: default
title: Workflow Discovery
parent: Architecture
nav_order: 3
---

# Workflow Discovery & Routing

This page covers the internal implementation of workflow discovery and routing. For the user-facing explanation of how to use the `WorkflowBus`, see [Mediator](../usage-guide/mediator.md).

## WorkflowRegistry

```csharp
public class WorkflowRegistry : IWorkflowRegistry
{
    public Dictionary<Type, Type> InputTypeToWorkflow { get; set; }

    public WorkflowRegistry(params Assembly[] assemblies)
    {
        // Scan assemblies for workflow implementations
        var workflowType = typeof(IServiceTrain<,>);
        var allWorkflowTypes = ScanAssembliesForWorkflows(assemblies, workflowType);

        // Create mapping: InputType -> WorkflowType
        InputTypeToWorkflow = allWorkflowTypes.ToDictionary(
            workflowType => ExtractInputType(workflowType),
            workflowType => workflowType
        );
    }
}
```

## WorkflowBus

```csharp
public class WorkflowBus : IWorkflowBus
{
    public async Task<TOut> RunAsync<TOut>(object workflowInput, Metadata? parentMetadata = null)
    {
        // 1. Find workflow type by input type
        var inputType = workflowInput.GetType();
        var workflowType = _registryService.InputTypeToWorkflow[inputType];

        // 2. Resolve workflow from DI container
        var workflow = _serviceProvider.GetRequiredService(workflowType);

        // 3. Inject internal workflow properties (framework-level only)
        _serviceProvider.InjectProperties(workflow);

        // 4. Set up parent-child relationship if needed
        if (parentMetadata != null)
            SetParentId(workflow, parentMetadata.Id);

        // 5. Execute workflow using reflection
        return await InvokeWorkflowRun<TOut>(workflow, workflowInput);
    }
}
```

## Key Constraints and Design Decisions

### Input Type Uniqueness

Each input type maps to exactly one workflow. This is enforced at startup by the `WorkflowRegistry`'s `ToDictionary` call—duplicate input types cause an exception. See [API Reference: AddServiceTrainBus]({{ site.baseurl }}{% link api-reference/configuration/add-effect-workflow-bus.md %}) for the full uniqueness rules and code examples.

### Workflow Discovery Rules

1. **Must be concrete classes** (not abstract)
2. **Must implement IServiceTrain<,>**
3. **Must have parameterless constructor or be registered in DI**
4. **Should implement a non-generic interface** for better DI integration
