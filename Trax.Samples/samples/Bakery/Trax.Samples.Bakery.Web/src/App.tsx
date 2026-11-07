import { useState } from "react";
import { Baker } from "./Baker";
import { Customer } from "./Customer";
import { connect, signIn, signOut, signedIn, type Person, type Role } from "./session";
import { ShopSign } from "./ShopSign";

const restored = signedIn();
if (restored) connect(restored);

/** Signs one person in, then shows their page: a customer's order, or a baker's shift, and the shop sign. */
export function App() {
  const [person, setPerson] = useState<Person | null>(restored);
  const [name, setName] = useState("");
  const [role, setRole] = useState<Role>("customer");
  const [error, setError] = useState<string | null>(null);

  if (!person)
    return (
      <main>
        <h1>Trax Bakery</h1>
        <form
          aria-label="Sign in"
          onSubmit={(e) => {
            e.preventDefault();
            signIn(name, role).then(
              (p) => {
                connect(p);
                setPerson(p);
              },
              (err: unknown) => setError(err instanceof Error ? err.message : String(err)),
            );
          }}
        >
          <label>
            Name <input value={name} onChange={(e) => setName(e.target.value)} aria-label="Name" required />
          </label>{" "}
          <label>
            Role{" "}
            <select value={role} onChange={(e) => setRole(e.target.value as Role)} aria-label="Role">
              <option value="customer">Customer</option>
              <option value="baker">Baker</option>
            </select>
          </label>{" "}
          <button type="submit">Sign in</button>
          {error && <p className="problem">{error}</p>}
        </form>
      </main>
    );

  return (
    <main>
      <h1>Trax Bakery</h1>
      <p>
        Signed in as {person.name}, a {person.role}.{" "}
        <button
          type="button"
          onClick={() => {
            signOut();
            location.reload();
          }}
        >
          Sign out
        </button>
      </p>
      <ShopSign />
      {person.role === "customer" ? <Customer /> : <Baker />}
    </main>
  );
}
