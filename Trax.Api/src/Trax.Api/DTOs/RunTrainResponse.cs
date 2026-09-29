using System.ComponentModel;

namespace Trax.Api.DTOs;

/// <summary>
/// The result of running a train through its generated GraphQL field when the train has no typed
/// output: only the id of the execution that ran.
/// </summary>
/// <param name="MetadataId">The id of the execution, which the execution queries take.</param>
[EditorBrowsable(EditorBrowsableState.Never)]
public record RunTrainResponse(long MetadataId);
