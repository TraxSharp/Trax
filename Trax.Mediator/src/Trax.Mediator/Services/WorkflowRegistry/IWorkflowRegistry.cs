using System;
using System.Collections.Generic;

namespace Trax.Mediator.Services.WorkflowRegistry;

/// <summary>
/// Defines a registry that maps workflow input types to their corresponding workflow types.
/// </summary>
/// <remarks>
/// The workflow registry is a key component of the mediator pattern implementation for workflows.
/// It maintains a dictionary that maps input types to workflow types, allowing the workflow bus
/// to dynamically discover and execute the appropriate workflow for a given input type.
///
/// The registry is typically populated during application startup by scanning assemblies for
/// workflow implementations and extracting their input types. This enables a type-based dispatch
/// mechanism where workflows are automatically discovered and registered without requiring
/// explicit registration code.
///
/// The registry is used by the workflow bus to look up the appropriate workflow type for a
/// given input object, which is then instantiated and executed.
/// </remarks>
public interface IWorkflowRegistry
{
    /// <summary>
    /// Gets or sets the dictionary that maps workflow input types to their corresponding workflow types.
    /// </summary>
    /// <remarks>
    /// This dictionary is the core data structure of the registry. It maps the type of an input object
    /// to the type of the workflow that can handle that input.
    ///
    /// The keys in this dictionary are the input types (e.g., OrderInput, CustomerInput),
    /// and the values are the corresponding workflow types (e.g., OrderWorkflow, CustomerWorkflow).
    ///
    /// This dictionary is typically populated during application startup by scanning assemblies
    /// for workflow implementations and extracting their input types.
    ///
    /// The workflow bus uses this dictionary to look up the appropriate workflow type for a
    /// given input object, which is then instantiated and executed.
    /// </remarks>
    public Dictionary<Type, Type> InputTypeToWorkflow { get; set; }
}
