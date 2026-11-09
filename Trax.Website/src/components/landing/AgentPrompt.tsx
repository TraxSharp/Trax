"use client";

import { useState } from "react";

const PROMPT = `Use Trax .NET (traxsharp.net) and React to build a live quiz for a room of people.
The host puts up one question at a time, players answer on their own screens, each
question closes 60 seconds after it opens, and a scoreboard shows the totals.

Write the app as features, not code: a glossary and a features/ folder in Gherkin
(traxsharp.net/docs/statemachine/write-machines-as-features). Trax derives the state
machines, the access rules and the API from them, so write no machine class and no access
rule by hand. Read the linked docs before you start.

What the features say:
- There is one quiz, owned by the system at a fixed key. Each player owns their answer
  sheet, and the host owns their hosting shift.
- When the host asks a question, every waiting answer sheet opens. After 60 seconds the
  question closes by itself, and the sheets close with it.
- An empty answer is refused with "Write something first.", an answer after the question
  closes is refused, and a player cannot ask a question.
- Scoring runs as a workflow when a question closes, and a scoreboard view shows the
  totals.

How to build it:
- Backend: \`dotnet new trax-hub\` (traxsharp.net/docs/reference/templates), switched to
  SQLite, with Development-only demo keys beside the template's: one host and two players.
- Derive the machines with \`trax machine from-gherkin\`, commit machines/ with its
  access.txt, and serve them with ServeFeatures.
- Frontend: \`npm create vite@latest -- --template react-ts\`. Generate its client with
  \`trax client\` from the manifest the host exports (traxsharp.net/docs/reference/cli)
  and use the generated hooks, with no hand-written fetch or GraphQL. One page for the
  host and one for a player, where a player picks their key from a list instead of
  signing in.

You are done when \`trax machine from-gherkin --check\` and \`trax client --check\` pass,
\`trax machine run\` is green, \`trax machine missing\` lists nothing and \`npm run build\`
has no errors. Then leave both running and give me the URLs for the host page, each
player's page and the operations console.`;

export default function AgentPrompt() {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(PROMPT);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // No clipboard (an insecure origin, say): the text is still there to select.
    }
  };

  return (
    <div className="min-w-0">
      <div className="flex items-baseline justify-between">
        <p className="text-sm text-text-muted">Prompt it yourself</p>
        <button
          type="button"
          onClick={copy}
          className="text-sm font-medium text-accent transition-colors hover:text-accent-hover"
        >
          {copied ? "Copied" : "Copy"}
        </button>
      </div>
      <div className="relative mt-3">
        <pre className="max-h-52 overflow-y-auto whitespace-pre-wrap border-l-2 border-accent/30 bg-bg-secondary py-4 pb-10 pl-5 pr-5 font-mono text-xs leading-relaxed text-text-secondary">
          {PROMPT}
        </pre>
        <div className="pointer-events-none absolute inset-x-0 bottom-0 h-10 bg-gradient-to-t from-bg-secondary to-transparent" />
      </div>
    </div>
  );
}
