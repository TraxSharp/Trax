using System;

namespace Trax.Core.Exceptions;

/// <summary>
/// Represents an exception that occurs during train execution.
/// This is the primary exception type used throughout the Trax.Core system.
/// </summary>
/// <remarks>
/// TrainException is used to:
/// 1. Signal errors in train configuration or execution
/// 2. Provide context about where and why the error occurred
/// 3. Propagate errors through the Railway-oriented programming pattern
///
/// When a step in a train fails, it returns a Left(Exception) in the Either monad,
/// which is typically a TrainException with details about the failure.
/// </remarks>
public class TrainException(string message) : Exception(message) { }
