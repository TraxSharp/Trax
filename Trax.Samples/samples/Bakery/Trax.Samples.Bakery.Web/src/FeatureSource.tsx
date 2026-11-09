import { useEffect, useMemo, useRef, useState } from "react";
import ordering from "../../features/ordering.feature?raw";
import shop from "../../features/shop.feature?raw";

/** The features the bakery's machines are derived from, as committed: the page shows the files, not a copy. */
const FILES = { "ordering.feature": ordering, "shop.feature": shop } as const;

export type FeatureFileName = keyof typeof FILES;

const KEYWORDS = /^(\s*)(Given|When|Then|And|But)(\s)/;
const HEADS = /^(\s*)(Feature|Background|Scenario Outline|Scenario|Rule|Examples):/;

function Line({ text }: { text: string }) {
  if (/^\s*#/.test(text)) return <span className="comment">{text}</span>;
  if (/^\s*@/.test(text)) return <span className="tag">{text}</span>;
  const head = HEADS.exec(text);
  if (head)
    return (
      <>
        {head[1]}
        <span className="kw-head">{head[2]}:</span>
        {text.slice(head[0].length)}
      </>
    );
  const step = KEYWORDS.exec(text);
  if (!step) return <>{text || " "}</>;
  const rest = text.slice(step[0].length);
  return (
    <>
      {step[1]}
      <span className="kw">{step[2]}</span>
      {step[3]}
      {rest.split(/("[^"]*")/).map((part, i) =>
        part.startsWith('"') ? (
          <span key={i} className="str">
            {part}
          </span>
        ) : (
          part
        ),
      )}
    </>
  );
}

/**
 * The feature files, with every scenario that ends where the person's machine is now lit, and the step that says so
 * marked. Nothing on the page restates a rule: the machines the host serves were derived from these lines.
 */
export function FeatureSource({ initial, phrases }: { initial: FeatureFileName; phrases: string[] }) {
  const key = phrases.join("|");
  // The file whose scenarios say where the machine is now, unless the person picked a tab since it last moved.
  const [picked, setPicked] = useState<{ key: string; file: FeatureFileName } | null>(null);
  const shown = useMemo(() => {
    const wanted = key ? key.split("|") : [];
    const names = Object.keys(FILES) as FeatureFileName[];
    const says = (f: FeatureFileName) => FILES[f].split("\n").some((l) => /^\s*(Then|And|But)\s/.test(l) && wanted.some((p) => l.endsWith(p)));
    return says(initial) ? initial : (names.find(says) ?? initial);
  }, [key, initial]);
  const file = picked?.key === key ? picked.file : shown;
  const lines = useMemo(() => FILES[file].replace(/\n$/, "").split("\n"), [file]);

  const marks = useMemo(() => {
    const wanted = key ? key.split("|") : [];
    const hit = lines.map((l) => /^\s*(Then|And|But)\s/.test(l) && wanted.some((p) => l.endsWith(p)));
    const lit = new Array<boolean>(lines.length).fill(false);
    // A block is a scenario or the background, from its tags down to the next block's.
    const starts: { at: number; scenario: boolean }[] = [];
    lines.forEach((l, i) => {
      if (!/^\s*(Scenario|Background)/.test(l)) return;
      let at = i;
      while (at > 0 && /^\s*@/.test(lines[at - 1])) at--;
      starts.push({ at, scenario: /^\s*Scenario/.test(l) });
    });
    starts.forEach(({ at, scenario }, n) => {
      const end = starts[n + 1]?.at ?? lines.length;
      if (scenario && hit.slice(at, end).some(Boolean)) for (let i = at; i < end; i++) lit[i] = true;
    });
    return { hit, lit, first: hit.indexOf(true) };
  }, [lines, key]);

  const first = useRef<HTMLDivElement | null>(null);
  // Scroll the code box alone, so the lit step sits in its middle; scrollIntoView would move the page too.
  useEffect(() => {
    const line = first.current;
    const box = line?.parentElement;
    if (line && box) box.scrollTo({ top: line.offsetTop - box.clientHeight / 2, behavior: "smooth" });
  }, [key, file]);

  return (
    <section className="panel source" aria-label="Feature source">
      <div className="source-head">
        Written as <code>features/</code>
        <div className="source-tabs" role="tablist">
          {(Object.keys(FILES) as FeatureFileName[]).map((f) => (
            <button
              key={f}
              type="button"
              role="tab"
              aria-selected={f === file}
              className={f === file ? "active" : ""}
              onClick={() => setPicked({ key, file: f })}
            >
              {f}
            </button>
          ))}
        </div>
      </div>
      <div className="gherkin">
        {lines.map((text, i) => (
          <div
            key={i}
            ref={i === marks.first ? first : undefined}
            className={marks.hit[i] ? "hit" : marks.lit[i] ? "lit" : undefined}
          >
            <span className="ln">{i + 1}</span>
            <span>
              <Line text={text} />
            </span>
          </div>
        ))}
      </div>
      <p className="source-foot">
        The lit scenarios end where your machine is now. The host serves what{" "}
        <code>trax machine from-gherkin</code> derived from these files: no machine class, no hand-written rule.
      </p>
    </section>
  );
}
