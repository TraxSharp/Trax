"use client";

import { useState } from "react";

const PROMPT = `Build me Battleship in the browser with Trax .NET (traxsharp.net) and React, against the
computer.

The server owns the game: both boards, the computer's ships and every shot are decided
there, so the page never sees where the computer's ships are. Read the linked docs before
writing code.

- Host: start from \`dotnet new trax-hub\` (traxsharp.net/docs/reference/templates) and
  switch it to SQLite.
- One Trax state machine per game (traxsharp.net/docs/statemachine): Placing, YourTurn,
  ComputerTurn, Won, Lost. Its guards decide what is allowed: ships must fit the 10x10 board
  without overlapping, and you can only fire on your turn at a square you haven't tried.
  Your shots and the computer's run on the server, in trains.
- Five ships (5, 4, 3, 3, 2). The computer hunts around a hit instead of firing at random.
- Frontend: \`npm create vite@latest -- --template react-ts\`, then generate its client with
  \`trax client\` from the manifest the host exports (traxsharp.net/docs/reference/cli).
  Use the generated hooks; write no fetch or GraphQL calls by hand. Show both boards with
  hits and misses, and the game's state diagram with the current state lit.
- No sign-in screen and no key to paste: the page uses the template's development demo
  key on its own. A reload resumes the game where it was.

Done when \`dotnet run\` and \`npm run dev\` play a whole game to a win or a loss;
\`trax client --check\` passes; and \`npm run build\` shows no errors. Then start both,
leave them running, and give me the URL to play it.`;

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
        <pre className="max-h-72 overflow-y-auto whitespace-pre-wrap border-l-2 border-accent/30 bg-bg-secondary py-4 pb-10 pl-5 pr-5 font-mono text-xs leading-relaxed text-text-secondary">
          {PROMPT}
        </pre>
        <div className="pointer-events-none absolute inset-x-0 bottom-0 h-10 bg-gradient-to-t from-bg-secondary to-transparent" />
      </div>
    </div>
  );
}
