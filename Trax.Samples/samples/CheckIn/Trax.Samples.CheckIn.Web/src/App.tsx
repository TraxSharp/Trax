import { useEffect, useState } from "react";
import { Agent } from "./Agent";
import { Departures } from "./Departures";
import { Passenger } from "./Passenger";
import { connect, signIn, signOut, signedIn, whenSignedOut, type Person, type Role } from "./session";

const restored = signedIn();
if (restored) connect(restored);

function Intro() {
  return (
    <div className="intro">
      <div className="eyebrow">
        <span className="mark">T</span>Trax · Check-in
      </div>
      <h1>Machines written as features</h1>
      <p className="subtitle">
        This check-in has no machine class. Its behaviour is two Gherkin files and a glossary, and{" "}
        <code>trax machine from-gherkin</code> derives the machines, who may do what, and how they react to each other.
        The page only asks the server to move them.
      </p>
    </div>
  );
}

/** Signs one person in, then shows their page: a passenger's booking, or a gate agent's desk, and the departures. */
export function App() {
  const [person, setPerson] = useState<Person | null>(restored);
  const [name, setName] = useState("");
  const [role, setRole] = useState<Role>("passenger");
  const [error, setError] = useState<string | null>(null);

  // A read refused for a person the host no longer knows (their token expired) signs them out: show the form again.
  useEffect(
    () =>
      whenSignedOut(() => {
        setPerson(null);
        setError("Your sign-in has expired. Sign in again.");
      }),
    [],
  );

  if (!person)
    return (
      <div className="signin-wrap">
        <main className="panel signin">
          <Intro />
          <form
            aria-label="Sign in"
            onSubmit={(e) => {
              e.preventDefault();
              signIn(name, role).then(
                (p) => {
                  connect(p);
                  setError(null);
                  setPerson(p);
                },
                (err: unknown) => setError(err instanceof Error ? err.message : String(err)),
              );
            }}
          >
            <label className="field">
              Name
              <input
                value={name}
                onChange={(e) => setName(e.target.value)}
                aria-label="Name"
                placeholder="Ada"
                autoFocus
                required
              />
            </label>
            <label className="field">
              Role
              <select value={role} onChange={(e) => setRole(e.target.value as Role)} aria-label="Role">
                <option value="passenger">Passenger: checks in for the flight</option>
                <option value="agent">Gate agent: opens check-in, closes the gate</option>
              </select>
            </label>
            <button type="submit" className="primary">
              Sign in
            </button>
            {error && (
              <p className="problem" role="alert">
                {error}
              </p>
            )}
          </form>
          <p className="aside-note">
            Open this page in two windows: a passenger in one, a gate agent in the other. Each person gets a token of
            their own, and sees only their own booking or desk.
          </p>
        </main>
      </div>
    );

  return (
    <div className="app">
      <aside className="panel sidebar">
        <Intro />
        <div className="group">
          <span className="group-label">Five words</span>
          <dl className="words">
            <div>
              <dt>Feature</dt>
              <dd>scenarios in plain words, the only place the behaviour is written.</dd>
            </div>
            <div>
              <dt>Glossary</dt>
              <dd>what the words mean: who the actors are, which phrases are states and moves.</dd>
            </div>
            <div>
              <dt>Machine</dt>
              <dd>derived from the scenarios. A move no scenario shows is refused.</dd>
            </div>
            <div>
              <dt>Policy</dt>
              <dd>the server moving one machine when another enters a state.</dd>
            </div>
            <div>
              <dt>View</dt>
              <dd>a read across machines, limited to what the reader may see.</dd>
            </div>
          </dl>
        </div>
        <div className="group">
          <span className="group-label">Signed in</span>
          <div className="person">
            <span className={`avatar tone-${person.role}`}>{person.name.slice(0, 1).toUpperCase()}</span>
            <div className="person-text">
              <strong>{person.name}</strong>
              <span>
                Signed in as {person.name}, {person.role === "agent" ? "a gate agent" : "a passenger"}.
              </span>
            </div>
          </div>
          <button
            type="button"
            className="ghost"
            onClick={() => {
              signOut();
              location.reload();
            }}
          >
            Sign out
          </button>
        </div>
        <p className="aside-note">
          {person.role === "passenger"
            ? "A passenger may give a passport number and check in on their own booking, and read the departures. Nothing else is served to them."
            : "A gate agent may open their own desk and close the gate there, and read the departures. They never touch a booking: the server moves those."}
        </p>
      </aside>
      <main className="stage">
        <Departures />
        {person.role === "passenger" ? <Passenger /> : <Agent />}
      </main>
    </div>
  );
}
