"use client";

import { useState } from "react";

const PROMPT = `Build me a trivia night in the browser with Trax .NET (traxsharp.net) and React: a
host runs rounds of questions, and players answer before each round closes.

Describe what the app does as features, not code: a glossary and a features/ folder in
Gherkin (traxsharp.net/docs/statemachine/write-machines-as-features). Trax derives the state
machines, who may do what, and the API from them. Write no machine class and no access rule
by hand. Read the linked docs before writing code.

- Host: start from \`dotnet new trax-hub\` (traxsharp.net/docs/reference/templates) and
  switch it to SQLite. Add Development-only demo keys beside the template's, one per person:
  a host and two players.
- The night is system-owned, at a fixed key. Each player owns their answer sheet, and the
  host owns their hosting shift.
- A round opens when the host asks the next question, and closes by itself after 60
  seconds. When a round opens, every waiting answer sheet can be answered; when it closes,
  none can.
- The scenarios say what is allowed and what is refused: an empty answer is refused with
  "Write something first.", an answer after the round closes is refused, and a player is
  not allowed to ask a question.
- Scoring is real work, so it goes in a train the closing round runs. Show a scoreboard
  view.
- Derive the machines with \`trax machine from-gherkin\`, commit machines/ with its
  access.txt, and serve them with ServeFeatures.
- Frontend: \`npm create vite@latest -- --template react-ts\`, then generate its client with
  \`trax client\` from the manifest the host exports (traxsharp.net/docs/reference/cli).
  Use the generated hooks; write no fetch or GraphQL calls by hand. One page for the host,
  one for a player; a player picks their key from a list, with no sign-in screen.

Done when \`trax machine from-gherkin --check\` passes, \`trax machine run\` is green,
\`trax machine missing\` lists nothing, \`trax client --check\` passes and \`npm run build\`
shows no errors. Then start both, leave them running, and give me the URLs for the host
and for each player.`;

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
