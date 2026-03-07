namespace Trax.Scheduler.Services.RunExecutor;

/// <summary>
/// HTTP wire contract for dispatching a synchronous run request to a remote endpoint.
/// </summary>
/// <param name="TrainName">The fully qualified service type name of the train to execute</param>
/// <param name="InputJson">JSON-serialized train input</param>
/// <param name="InputType">Fully-qualified type name for deserializing <paramref name="InputJson"/></param>
public record RemoteRunRequest(string TrainName, string InputJson, string InputType);
