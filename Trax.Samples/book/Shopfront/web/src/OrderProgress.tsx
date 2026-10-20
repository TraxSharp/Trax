import { useRunProgress } from "./trax/index.g";

// Follows the run that placed the order, step by step, as the hub records it.
export function OrderProgress({ runId }: { runId: string }) {
  const run = useRunProgress(runId);

  return (
    <section>
      <h2>Your order: {run.state ?? "..."}</h2>
      <ol>
        {run.steps.map((step) => (
          <li key={step.position}>
            {step.name}: {step.state}
          </li>
        ))}
      </ol>
      {run.failure && <p role="alert">{run.failure}</p>}
    </section>
  );
}
