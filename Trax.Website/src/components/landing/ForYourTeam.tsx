import FeatureWalk from "./FeatureWalk";
import CodeFold from "./CodeFold";
import DemoSection from "./DemoSection";

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
    <DemoSection
      title="Everyone reads the same features"
      intro={
        <p>
            The feature files are the spec, the tests and the access rules at once, so everyone reads the same thing. Step
            through a flight check-in to see what each phrase becomes in the machines Trax derives from it.
        </p>
      }
      links={[
        { href: "/docs/statemachine/write-machines-as-features", label: "Write machines as features" },
        { href: "/docs/reference/feature-files", label: "What each step means" },
      ]}
      source={{
        folder: "CheckIn",
        files: [
          { path: "features/checking-in.feature", label: "the scenarios shown above" },
          { path: "features/gate.feature", label: "the gate agent and the flight's timers" },
          { path: "features/glossary.json", label: "the words: actors, states, phrases, guards, views" },
          { path: "machines/booking.ir.json", label: "the booking machine, derived" },
          { path: "machines/access.txt", label: "every grant, derived, one line each" },
          { path: "Trax.Samples.CheckIn.Api/Trains/IssueBoardingPassTrain.cs", label: "the one piece of C# behind the machines" },
        ],
        docs: "/docs/statemachine/write-machines-as-features",
        run: ["samples/CheckIn/demo.sh", "open http://localhost:5281"],
      }}
      after={
        <div className="mt-4">
          <CodeFold label="What each person on the team gets from the features">
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {roles.map((r) => (
                <div key={r.role} className="rounded-lg border border-border bg-bg-primary p-5">
                  <h3 className="text-sm font-semibold text-text-primary">{r.role}</h3>
                  <p className="mt-2 text-sm leading-relaxed text-text-secondary">{r.body}</p>
                </div>
              ))}
            </div>
          </CodeFold>
        </div>
      }
    >
      <FeatureWalk />
    </DemoSection>
  );
}
