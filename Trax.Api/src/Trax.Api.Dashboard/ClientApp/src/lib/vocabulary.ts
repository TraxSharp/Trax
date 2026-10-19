// The console's words for Trax's own terms (api/0043): Trax's names stay, and each is explained
// where a page leads with it, linking to its own entry in the glossary on traxsharp.net. Each entry
// is a heading there, and its link is written out whole so the docs build can check it lands
// (Trax.Docs CodeLinksToDocsResolveTests).

export const GLOSSARY = "https://traxsharp.net/docs/reference/glossary";

export interface Term {
  /** How the term reads in a sentence. */
  name: string;
  /** One plain sentence: what it is, for someone who has not read the docs. */
  means: string;
  /** Its glossary entry: GLOSSARY and the anchor of the term's heading. */
  glossary: string;
}

export const TERMS = {
  train: {
    name: "train",
    means: "A unit of work with one input and one output that runs a chain of steps.",
    glossary: "https://traxsharp.net/docs/reference/glossary#train",
  },
  run: {
    name: "run",
    means: "One execution of a train, from its input to its output or the failure that stopped it.",
    glossary: "https://traxsharp.net/docs/reference/glossary#run",
  },
  junction: {
    name: "junction",
    means: "One step of a train's chain.",
    glossary: "https://traxsharp.net/docs/reference/glossary#junction",
  },
  manifest: {
    name: "manifest",
    means: "A schedule for a train: which train, with what input, when it runs, and how failures are retried.",
    glossary: "https://traxsharp.net/docs/reference/glossary#manifest",
  },
  manifestGroup: {
    name: "manifest group",
    means: "A set of manifests that share a priority, a limit on how many run at once, and an on/off switch.",
    glossary: "https://traxsharp.net/docs/reference/glossary#manifest-group",
  },
  workQueue: {
    name: "work queue",
    means: "Runs that were asked for and are waiting for a worker to pick them up.",
    glossary: "https://traxsharp.net/docs/reference/glossary#work-queue",
  },
  deadLetter: {
    name: "dead letter",
    means: "A manifest whose runs kept failing until its retries ran out. It waits for someone to requeue or acknowledge it.",
    glossary: "https://traxsharp.net/docs/reference/glossary#dead-letter",
  },
  machine: {
    name: "state machine",
    means: "A record that moves between named states when it receives commands, and can start trains as it goes.",
    glossary: "https://traxsharp.net/docs/reference/glossary#state-machine",
  },
  effect: {
    name: "effect",
    means: "Something that runs around every train run, such as recording it to the database or logging.",
    glossary: "https://traxsharp.net/docs/reference/glossary#effect",
  },
  bulkOperation: {
    name: "bulk operation",
    means: "One action applied to every row a list's filter matched when it started, in batches, with what did not take and why.",
    glossary: "https://traxsharp.net/docs/reference/glossary#bulk-operation",
  },
  decider: {
    name: "decider",
    means: "What answers a train's questions, such as a model. Its answers are recorded and can be replayed.",
    glossary: "https://traxsharp.net/docs/reference/glossary#decider",
  },
} satisfies Record<string, Term>;

export type TermKey = keyof typeof TERMS;
