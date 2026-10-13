namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet;

/// <summary>
/// How the greeting addresses the caller. The stored <c>greet_v1</c> document picks one, so an
/// operator changes the answer by re-uploading the document, not by shipping a new app.
/// </summary>
public enum GreetingStyle
{
    Casual,
    Formal,
}
