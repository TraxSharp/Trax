import { Arrow, Figure } from "./Diagram";

// A scenario from the Bakery sample's ordering feature, and what the team gets from it, in plain words. Each phrase
// is coloured like the box it produces, and each box quotes the words it comes from. The test is the whole scenario,
// so its colour frames the scenario rather than a phrase. The home page shows no code outside the samples.
type Part = "who" | "call" | "rule";

const TEXT: Record<Part, string> = {
  who: "text-info",
  call: "text-signal",
  rule: "text-derail",
};

const BOX: Record<Part | "test", string> = {
  who: "border-info/50 bg-info/10",
  call: "border-signal/50 bg-signal/10",
  rule: "border-derail/50 bg-derail/10",
  test: "border-accent/50 bg-accent/10",
};

const LABEL: Record<Part | "test", string> = { ...TEXT, test: "text-accent" };

// One line of the scenario: its keyword, then plain and coloured pieces.
const SCENARIO: { keyword: string; pieces: (string | [Part, string])[] }[] = [
  { keyword: "Given", pieces: ["Ada ", ["who", "is a customer"]] },
  { keyword: "And", pieces: ["Ada's order ", ["rule", "has nothing in it"]] },
  { keyword: "When", pieces: ["Ada ", ["call", "places her order"]] },
  { keyword: "Then", pieces: ["Ada is told ", ["rule", "“Add something first.”"]] },
];

const OUTPUTS: { part: Part | "test"; label: string; from: string; body: string }[] = [
  {
    part: "who",
    label: "Who may do it",
    from: "“is a customer”",
    body: "Customers can place orders. Nobody the scenarios don't show doing it can.",
  },
  {
    part: "call",
    label: "The API",
    from: "“places her order”",
    body: "A call for placing an order, and a typed client for the app to make it with.",
  },
  {
    part: "rule",
    label: "The rule",
    from: "“has nothing in it” and the message",
    body: "An empty order is refused, and the person is told why.",
  },
  {
    part: "test",
    label: "The test",
    from: "the whole scenario",
    body: "The same sentences, run against the real server.",
  },
];

export default function FeatureToMachine() {
  return (
    <Figure>
      <div className="flex flex-col items-stretch gap-3 lg:flex-row lg:items-center">
        <div className={`rounded-md border px-4 py-3 lg:w-80 ${BOX.test}`}>
          <p className="text-xs text-text-muted">An empty order is refused</p>
          <ul className="mt-2 space-y-1 text-sm text-text-secondary">
            {SCENARIO.map((line, n) => (
              <li key={n}>
                <span className="font-semibold text-text-primary">{line.keyword}</span>{" "}
                {line.pieces.map((piece, i) =>
                  typeof piece === "string" ? (
                    <span key={i}>{piece}</span>
                  ) : (
                    <span key={i} className={`font-medium ${TEXT[piece[0]]}`}>
                      {piece[1]}
                    </span>
                  ),
                )}
              </li>
            ))}
          </ul>
        </div>
        <div className="lg:hidden">
          <Arrow direction="down" />
        </div>
        <div className="hidden lg:block">
          <Arrow direction="right" />
        </div>
        <div className="grid flex-1 gap-3 sm:grid-cols-2">
          {OUTPUTS.map((o) => (
            <div key={o.part} className={`rounded-md border px-3 py-2 ${BOX[o.part]}`}>
              <p className={`text-sm font-medium ${LABEL[o.part]}`}>{o.label}</p>
              <p className="mt-0.5 text-[11px] text-text-muted">from {o.from}</p>
              <p className="mt-1 text-xs leading-relaxed text-text-secondary">{o.body}</p>
            </div>
          ))}
        </div>
      </div>
    </Figure>
  );
}
