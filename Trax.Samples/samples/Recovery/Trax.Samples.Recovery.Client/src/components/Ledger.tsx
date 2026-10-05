import { PROVENANCE_TEXT, answerMs, provenanceOf, questionStep, questionsOf, routeStep } from "../routes";
import type { Attempt, Scenario } from "../types";

interface Props {
  scenario: Scenario;
  attempts: Attempt[];
  labelOf(attempt: Attempt, index: number): string;
}

const seconds = (ms: number) => (ms >= 1000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms)} ms`);

/**
 * Every answer the train needed, one row per question and one column per attempt: what it was, where it
 * came from, how long it took, and the hash of the state it was about.
 */
export function Ledger({ scenario, attempts, labelOf }: Props) {
  const questions = questionsOf(scenario);

  let asked = 0;
  let replayed = 0;
  let savedMs = 0;
  attempts.forEach((a, index) =>
    questions.forEach((q) => {
      const step = questionStep(a, q.key);
      if (!step) return;
      if (step.replayed) {
        replayed++;
        // What the replayed answer would have cost: the same question's time in the attempt it came from.
        const source = attempts.find((x) => x.id === a.journal?.replayDecisionsOf) ?? attempts[index - 1];
        const original = source && questionStep(source, q.key);
        if (source && original) savedMs += answerMs(source, original) ?? 0;
      } else asked++;
    }),
  );

  return (
    <section className="panel ledger">
      <header className="panel-head">
        <h2>Decisions</h2>
        <div className="tally">
          <span className="tally-item model">
            <b>{asked}</b> model call{asked === 1 ? "" : "s"}
          </span>
          <span className="tally-item replayed">
            <b>{replayed}</b> replayed{savedMs > 0 ? `, ${seconds(savedMs)} of model time not spent again` : ""}
          </span>
        </div>
      </header>
      {attempts.length === 0 ? (
        <p className="hint">
          Each answer the train asks for lands here, with where it came from. Run a case with a crash to watch a
          retry reuse them.
        </p>
      ) : (
        <div className="ledger-scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">Question</th>
                {attempts.map((a, index) => (
                  <th key={a.id} scope="col">
                    {labelOf(a, index)}
                    <span className="exec">execution {a.id}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {questions.map((q) => (
                <tr key={q.key}>
                  <th scope="row">
                    <code>{q.key}</code>
                    <span className="question-label">{q.label}</span>
                  </th>
                  {attempts.map((a, index) => {
                    const step = questionStep(a, q.key);
                    const route = routeStep(a, q.key);
                    if (!step) {
                      const ended = a.trainState !== "IN_PROGRESS" && a.trainState !== "PENDING";
                      return (
                        <td key={a.id} className="cell empty">
                          {ended ? "not reached" : <span className="waiting">waiting</span>}
                        </td>
                      );
                    }
                    const provenance = provenanceOf(a, index, step);
                    const ms = answerMs(a, step);
                    const hash = a.journal?.decisions.find((d) => d.questionKey === q.key)?.stateHash;
                    const value = step.answerWithheld ? "(withheld)" : route?.answer ?? step.answer;
                    const score = step.kind !== "CHOICE" && step.answer && step.answer !== value ? step.answer : null;
                    return (
                      <td key={a.id} className={`cell ${provenance}`}>
                        <div className="answer">
                          {value}
                          {score && <span className="score">{step.kind === "YES_NO" ? `p ${score}` : `score ${score}`}</span>}
                        </div>
                        <div className="provenance">{PROVENANCE_TEXT[provenance]}</div>
                        <div className="meta">
                          {ms != null && <span>{seconds(ms)}</span>}
                          {hash && <span title={`state hash ${hash}`}>state {hash.replace(/^[^:]*:/, "").slice(0, 8)}</span>}
                        </div>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
