using System.Text.Json.Nodes;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// The fields of a train's input and output, as the host reads them from its records: each a field in the IR's shape,
/// sorted by name. Null fields mean the train was not found, with <see cref="Problem"/> saying why.
/// </summary>
internal sealed record TrainShape(JsonArray? Input, JsonArray? Output, string? Problem);
