// What the example on sdk-reference/statemachine-api/machine-instances.md takes as given: a
// system-owned machine with a retry out of its failure state.
public enum IngestState
{
    Waiting,
    Failed,
}

public enum IngestTrigger
{
    Retry,
}

public sealed class IngestMachine : Machine<IngestState, IngestTrigger>
{
    protected override void Configure(IMachineBuilder<IngestState, IngestTrigger> m)
    {
        m.Id("ingest")
            .Version(1)
            .StartsAt(IngestState.Waiting, () => new JsonObject())
            .SystemOwned();

        m.In(IngestState.Failed).Context().On(IngestTrigger.Retry).To(IngestState.Waiting);
        m.In(IngestState.Waiting).Context();
    }
}
