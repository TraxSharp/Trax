using System;
using System.Collections.Generic;

namespace Trax.Mediator.Services.TrainRegistry;

/// <summary>
/// Defines a registry that maps train input types to their corresponding train types.
/// </summary>
/// <remarks>
/// The train registry is a key component of the mediator pattern implementation for trains.
/// It maintains a dictionary that maps input types to train types, allowing the train bus
/// to dynamically discover and execute the appropriate train for a given input type.
///
/// The registry is typically populated during application startup by scanning assemblies for
/// train implementations and extracting their input types. This enables a type-based dispatch
/// mechanism where trains are automatically discovered and registered without requiring
/// explicit registration code.
///
/// The registry is used by the train bus to look up the appropriate train type for a
/// given input object, which is then instantiated and executed.
/// </remarks>
public interface ITrainRegistry
{
    /// <summary>
    /// Gets or sets the dictionary that maps train input types to their corresponding train types.
    /// </summary>
    /// <remarks>
    /// This dictionary is the core data structure of the registry. It maps the type of an input object
    /// to the type of the train that can handle that input.
    ///
    /// The keys in this dictionary are the input types (e.g., OrderInput, CustomerInput),
    /// and the values are the corresponding train types (e.g., OrderTrain, CustomerTrain).
    ///
    /// This dictionary is typically populated during application startup by scanning assemblies
    /// for train implementations and extracting their input types.
    ///
    /// The train bus uses this dictionary to look up the appropriate train type for a
    /// given input object, which is then instantiated and executed.
    /// </remarks>
    public Dictionary<Type, Type> InputTypeToTrain { get; set; }
}
