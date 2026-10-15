// The client format: the contract between the code `trax client` generates and the runtime it calls
// (createMachineServer, useServerMachine, createMachineExecutor and the rest). Generated code records the format it was
// generated for and asks the installed package to confirm it serves that format on load, so a runtime whose API moved
// fails there, naming both, rather than at whichever call changed.
//
// The IR format (rules/irFormat.ts) covers the twin; this covers everything around it. Bump clientFormat when an API
// the generator targets changes, together with ClientGenerator.ClientFormat in Trax.Cli, which ClientFormatParityTests
// holds equal to it. Raise oldestClientFormatServed only when the runtime stops serving an older format. Generated
// code asks for this check before it loads any twin, so a stale client is told about both formats in this order.

/** The client format this runtime serves and the generator writes. */
export const clientFormat = 2;

/** The oldest client format this runtime still serves. */
export const oldestClientFormatServed = 1;

/**
 * Throws unless this runtime serves the client format `generated` names, naming both and saying whether to upgrade
 * the package or regenerate the client. Generated code calls it once, when it loads.
 */
export function requireClientFormat(generated: number): void {
  if (!Number.isInteger(generated))
    throw new Error(
      `This generated client names its format as ${JSON.stringify(generated)}, which is not a format number. ` +
        'Regenerate it (trax client).',
    );
  if (generated > clientFormat)
    throw new Error(
      `This client was generated for client format ${generated}, but the installed @traxsharp/state-machine serves ` +
        `only up to format ${clientFormat}. Upgrade @traxsharp/state-machine to the version of the trax tool that ` +
        'generated the client, or regenerate it with the trax tool matching the installed package (trax client).',
    );
  if (generated < oldestClientFormatServed)
    throw new Error(
      `This client was generated for client format ${generated}, which the installed @traxsharp/state-machine no ` +
        `longer serves (formats ${oldestClientFormatServed} to ${clientFormat}). Regenerate it with the trax tool ` +
        'matching the installed package (trax client).',
    );
}
