import { useState } from "react";
import { Avatar } from "./Avatar";
import { Diagram } from "./Diagram";
import { Inspector } from "./Inspector";
import { SideEffect } from "./SideEffect";
import { CHECKOUT, TURNSTILE, type MachineView } from "./machines";
import { signIn, type User } from "./connection";
import { useMachineStage, type MachineStageHandle } from "./useMachineStage";
import { Walkthrough } from "./Walkthrough";

const USERS: User[] = [
  { label: "Alice", key: "alice-key-do-not-use-in-production" },
  { label: "Bob", key: "bob-key-do-not-use-in-production" },
];

const WORDS = [
  ["State", "where the machine is now. One at a time."],
  ["Trigger", "a request to move. It only means something if an arrow with its name leaves the current state."],
  ["Guard", "a rule a move must pass to happen."],
  ["Rule", "what every draft in a state must hold, checked whenever a draft lands there."],
  ["Effect", "something irreversible a move does, such as a charge. It runs exactly once."],
];

const MACHINES = [TURNSTILE, CHECKOUT];

export function App() {
  const [user, setUser] = useState(() => {
    signIn(USERS[0]);
    return USERS[0];
  });
  const [active, setActive] = useState<MachineView>(TURNSTILE);
  const [inspecting, setInspecting] = useState(false);

  return (
    <div className="app">
      <aside className="sidebar">
        <div className="intro">
          <span className="eyebrow">
            <span className="mark">T</span>
            Trax · State Machine
          </span>
          <h1>What a state machine does</h1>
          <p className="subtitle">
            A state machine says which states something can be in, and which moves between them are allowed.
            Here the server holds the real state: the page only asks it to move, and the server decides.
          </p>
        </div>

        <div className="group">
          <span className="group-label">Five words</span>
          <dl className="words">
            {WORDS.map(([word, meaning]) => (
              <div key={word}>
                <dt>{word}</dt>
                <dd>{meaning}</dd>
              </div>
            ))}
          </dl>
        </div>

        <div className="group">
          <span className="group-label">Signed in as</span>
          <div className="user-selector user-selector-2" role="radiogroup" aria-label="Signed in as">
            {USERS.map((u) => (
              <button
                key={u.key}
                role="radio"
                aria-checked={u.key === user.key}
                className={u.key === user.key ? "active" : ""}
                onClick={() => {
                  signIn(u);
                  setUser(u);
                }}
              >
                <Avatar name={u.label} size="sm" />
                {u.label}
              </button>
            ))}
          </div>
          <p className="aside-note">Each person has their own draft of each machine.</p>
        </div>

        <button className={`hood-toggle ${inspecting ? "on" : ""}`} onClick={() => setInspecting(!inspecting)}>
          <span className="hood-icon">{"</>"}</span>
          {inspecting ? "Hide the raw requests" : "Show the raw requests"}
        </button>
      </aside>

      <main className="stage">
        <nav className="tabs" role="tablist">
          {MACHINES.map((m, i) => (
            <button
              key={m.name}
              role="tab"
              aria-selected={active.name === m.name}
              className={active.name === m.name ? "tab active" : "tab"}
              onClick={() => setActive(m)}
            >
              <span className="tab-number">{i + 1}</span>
              <span>
                <span className="tab-title">{m.title}</span>
                <span className="tab-tagline">{m.tagline}</span>
              </span>
            </button>
          ))}
        </nav>
        <MachineStage key={`${active.name}-${user.key}`} machine={active} user={user.label} />
      </main>

      {inspecting && <Inspector onClose={() => setInspecting(false)} />}
    </div>
  );
}

function MachineStage({ machine, user }: { machine: MachineView; user: string }) {
  const m = useMachineStage(machine.name, user);

  return (
    <div className="stage-body">
      <section className="card machine">
        <div className="machine-head">
          <div>
            <h2>
              <code>{machine.name}</code>
            </h2>
            <p className="example">{machine.example}</p>
          </div>
          <span className="badge good">{m.state ?? "…"}</span>
        </div>

        <Diagram machine={machine} current={m.state} />

        <div className="group">
          <span className="group-label">Fire a trigger</span>
          <div className="triggers">
            {machine.triggers.map((t) => {
              const available = machine.moves.some((mv) => mv.trigger === t.trigger && mv.from === m.state);
              return (
                <button
                  key={`${t.trigger}-${t.caption}`}
                  className={`trigger ${available ? "avail" : ""} ${t.probe ? "probe" : ""}`}
                  disabled={m.busy}
                  onClick={() => void (t.send ? m.send() : m.advance(t.trigger, t.input))}
                >
                  <span className="trigger-name">
                    {t.trigger}
                    {t.input && <span className="trigger-input">{JSON.stringify(t.input).replace(/"(\w+)":/g, "$1: ")}</span>}
                  </span>
                  <span className="trigger-caption">{t.caption}</span>
                </button>
              );
            })}
          </div>
          <p className="aside-note">
            Every trigger can be sent from any state. The highlighted ones have an arrow out of {m.state ?? "…"}; the
            others show what the server says when there isn't one.
          </p>
        </div>

        {machine.name === "checkout" && <AddItem m={m} />}

        <Context m={m} />
      </section>

      <div className="side">
        <section className="card explain">
          <h3>What the server just did</h3>
          <Walkthrough machine={machine} last={m.last} user={user} />
          <details className="snapshot">
            <summary>The draft the server holds for {user}</summary>
            <pre>{m.snapshot ? JSON.stringify(m.snapshot, null, 2) : "loading…"}</pre>
          </details>
        </section>
        {machine.moves.some((mv) => mv.effect) && <SideEffect last={m.last} draftId={m.id ?? ""} />}
      </div>
    </div>
  );
}

/** The cart is filled with the AddItem event: the page names the item, and the server prices the cart. */
function AddItem({ m }: { m: MachineStageHandle }) {
  const [item, setItem] = useState("");

  const add = () => {
    if (!item.trim()) return;
    void m.advance("AddItem", { item: item.trim() });
    setItem("");
  };

  return (
    <div className="group">
      <span className="group-label">Fill the cart</span>
      <div className="editor">
        <input
          value={item}
          placeholder="Add an item ($9.99)"
          onChange={(e) => setItem(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && add()}
        />
        <button disabled={m.busy || !item.trim()} onClick={add}>
          AddItem
        </button>
      </div>
      <p className="aside-note">
        The page sends only the item&apos;s name. The server appends it and computes the total, so no request can set
        a price.
      </p>
    </div>
  );
}

/** What the current state carries, in a line. */
function Context({ m }: { m: MachineStageHandle }) {
  const entries = Object.entries(m.context);
  return (
    <div className="context">
      <span className="group-label">Context</span>
      {entries.length === 0 ? (
        <span className="context-empty">empty</span>
      ) : (
        <div className="context-items">
          {entries.map(([k, v]) => (
            <span key={k} className="context-item">
              <span className="context-key">{k}</span>
              <span className="context-value">
                {k === "total" && typeof v === "number" ? `$${(v / 100).toFixed(2)}` : JSON.stringify(v)}
              </span>
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
