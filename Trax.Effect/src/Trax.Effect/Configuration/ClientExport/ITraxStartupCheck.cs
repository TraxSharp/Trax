namespace Trax.Effect.Configuration.ClientExport;

/// <summary>
/// A hosted service that only checks the host's configuration and its own types at startup, and refuses to start a
/// host that is wrong: it never reads a store, opens a connection or calls another service. A host exporting its
/// client manifest runs every one of these and no other hosted service, so an export refuses whatever production
/// refuses and touches no database.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/client-manifest">SDK reference</seealso>
public interface ITraxStartupCheck;
