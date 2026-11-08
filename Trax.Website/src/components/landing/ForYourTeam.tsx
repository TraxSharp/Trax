import FeatureToMachine from "./FeatureToMachine";

// Who reads the feature files, and what each of them gets from them. Each line says only what ships.
const roles: { role: string; body: string }[] = [
  {
    role: "Product",
    body: "Writes the behaviour as scenarios in plain English, and sees which situations nobody has decided yet.",
  },
  {
    role: "QA",
    body: "The features are the test suite. They run in memory and against the real host, with no step code to write.",
  },
  {
    role: "Developers",
    body: "Write the steps that do real work, like taking a payment, and the UI. The machines, access rules, API and typed clients come from the features.",
  },
  {
    role: "Reviewers",
    body: "A pull request shows the sentences that changed, and a list of every permission they grant.",
  },
  {
    role: "Operators",
    body: "The console's board links each rule on a running machine back to the scenarios that produced it.",
  },
  {
    role: "Agents",
    body: "Write features, which they do well. Trax derives the machines the same way every time, and the server checks the agent as it checks anyone.",
  },
];

export default function ForYourTeam() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">Everyone reads the same features</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          The feature files are the spec, the tests and the access rules at once, so everyone reads the same thing.
        </p>
        <FeatureToMachine />
        <div className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {roles.map((r) => (
            <div key={r.role} className="rounded-lg border border-border bg-bg-secondary p-5">
              <h3 className="text-sm font-semibold text-text-primary">{r.role}</h3>
              <p className="mt-2 text-sm leading-relaxed text-text-secondary">{r.body}</p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
