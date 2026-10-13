import { useLayoutEffect, useRef } from "react";
import type { ModelCall } from "../workflowView";

/** What the model was asked and what it answered, one card per question, newest at the bottom. */
export function ModelPanel({ calls }: { calls: ModelCall[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the panel itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [calls.length]);
  const model = calls.find((c) => c.model)?.model;

  return (
    <div className="panel model-panel">
      <div className="panel-title">
        Model
        {model && <span className="panel-aside mono">{model}</span>}
      </div>
      <div ref={box} className="model-calls">
        {calls.map((call) => (
          <div key={call.key} className="model-call" data-state={call.state}>
            <div className="model-call-head">
              <span className="mono">{call.questionKey}?</span>
              <span className="model-call-attempt">{call.attempt}</span>
              {call.badge && <span className={`badge ${call.badge.tone}`}>{call.badge.text}</span>}
            </div>
            {call.question && (
              <div className="model-question">
                <p>{call.question.instructions}</p>
                <ul>
                  {(call.question.options ?? [
                    { name: "Yes", description: call.question.yes },
                    { name: "No", description: call.question.no },
                  ]).map((o) => (
                    <li key={o.name}>
                      <span className="mono">{o.name}</span>
                      {o.description ? `: ${o.description}` : ""}
                    </li>
                  ))}
                </ul>
              </div>
            )}
            <div className="model-answer">
              {call.state === "waiting" ? (
                <span className="waiting">Waiting for the model…</span>
              ) : (
                <>
                  <span className="answer">&rarr; {call.answer}</span>
                  {call.ms != null && <span className="took">{(call.ms / 1000).toFixed(1)} s</span>}
                </>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
