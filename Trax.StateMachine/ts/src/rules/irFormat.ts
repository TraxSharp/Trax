// The IR format: the contract between the IR the C# engine exports (IrExporter) and the TypeScript engine that
// interprets it, including the factory call a generated twin makes. A twin generated with specifier imports runs on
// whichever @traxsharp/state-machine the consumer installed, which need not be the engine that generated it, so it
// carries the format it was generated for and the engine refuses one it cannot read.
//
// The package's version is no measure of this: every Trax release bumps it, most without touching the IR. The format
// changes only when the contract does. Bump irFormatVersion when the IR gains something an engine reading the
// previous format would misread or ignore (a new section, rule or reduction, a changed meaning), together with
// IrExporter.FormatVersion in Trax.Effect.StateMachine, which IrFormatParityTests holds equal to it. Raise
// oldestIrFormatRead only when this engine stops reading an older format.

/** The IR format this engine reads and its generators write. */
export const irFormatVersion = 1;

/** The oldest IR format this engine still reads. */
export const oldestIrFormatRead = 1;

/**
 * Throws unless this engine reads the IR format `irFormat` names. An IR without one (a committed `ir.json`, or a twin
 * generated before the format was recorded) is not checked.
 */
export function requireReadableIrFormat(irFormat: unknown): void {
  if (irFormat === undefined) return;
  if (typeof irFormat !== "number" || !Number.isInteger(irFormat))
    throw new Error(
      `This state-machine IR names its format as ${JSON.stringify(irFormat)}, which is not a format number. ` +
        "Regenerate the twin (trax machine generate).",
    );
  if (irFormat > irFormatVersion)
    throw new Error(
      `This state-machine twin was generated for IR format ${irFormat}, but the installed ` +
        `@traxsharp/state-machine reads only up to format ${irFormatVersion}. Upgrade @traxsharp/state-machine to the ` +
        "version of the trax tool that generated the twin, or regenerate the twin with the trax tool matching " +
        "the installed package (trax machine generate).",
    );
  if (irFormat < oldestIrFormatRead)
    throw new Error(
      `This state-machine twin was generated for IR format ${irFormat}, which the installed ` +
        `@traxsharp/state-machine no longer reads (formats ${oldestIrFormatRead} to ${irFormatVersion}). ` +
        "Regenerate the twin with the trax tool matching the installed package (trax machine generate).",
    );
}
