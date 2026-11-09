"use client";

import { useState } from "react";

const PROMPT = `Use Trax .NET (traxsharp.net) and React to build Battleship in the browser: one
player against the computer.

Write the game as features, not code: a glossary and a features/ folder in Gherkin
(traxsharp.net/docs/statemachine/write-machines-as-features). Trax derives the state
machine, its access rules and its API from them, so write no machine class and no access
rule by hand. Read that page and the CheckIn sample it links before you start: the game
has the same shape.

What the features say:
- Each player owns their games. A new game deals both fleets at random: five ships
  (5, 4, 3, 3, 2) each on a 10x10 board, none overlapping.
- On your turn you fire at a square and learn whether it was a miss, a hit or a sunk
  ship. Firing at a square you have already tried costs you nothing. Then the computer
  fires back, hunting around a hit instead of firing at random.
- You win when the computer's last ship sinks, and lose when yours does.

The server owns the game. Dealing, your shot and the computer's shot are workflows the
game's states invoke. Everything in the game's context reaches the page, so the
computer's fleet stays on the server, out of the context.

How to build it:
- Backend: \`dotnet new trax-hub\` (traxsharp.net/docs/reference/templates), switched to
  SQLite. Derive the machine with \`trax machine from-gherkin\`, commit machines/ with
  its access.txt, and serve it with ServeFeatures.
- Frontend: \`npm create vite@latest -- --template react-ts\`. Generate its client with
  \`trax client\` from the manifest the host exports (traxsharp.net/docs/reference/cli)
  and use the generated hooks, with no hand-written fetch or GraphQL. Show both boards
  with hits and misses, and whose turn it is. No sign-in: the page uses the template's
  development demo key, and a reload resumes the game.

You are done when \`trax machine from-gherkin --check\` and \`trax client --check\` pass,
\`trax machine run\` is green, \`trax machine missing\` lists nothing, \`npm run build\`
has no errors, and you have played a whole game in a browser against the running host,
to a win or a loss. Then leave both running and give me the URL to play it.`;

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
