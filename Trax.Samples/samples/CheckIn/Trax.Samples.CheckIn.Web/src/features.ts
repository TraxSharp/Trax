import glossaryJson from "../../features/glossary.json";

interface Glossary {
  machines: Record<string, { noun?: string; name?: string; states: Record<string, string | { phrase: string }> }>;
}

const glossary = glossaryJson as unknown as Glossary;

/**
 * How a step says a machine is in a state, from the glossary: "booking is checked in" for a user's machine (a step reads
 * "Ada's booking is checked in"), "the flight is closed" for the system's.
 */
export function statePhrase(machine: string, state: string | undefined): string | null {
  const m = glossary.machines[machine];
  const shape = state ? m?.states[state] : undefined;
  const phrase = typeof shape === "string" ? shape : shape?.phrase;
  if (!m || !phrase) return null;
  return `${m.noun ?? m.name} ${phrase}`;
}
