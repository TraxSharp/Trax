import { codeToHtml } from "shiki";

interface CodeBlock {
  label: string;
  code: string;
}

interface Architecture {
  name: string;
  description: string;
  blocks: CodeBlock[];
}

const architectures: Architecture[] = [
  {
    name: "All-in-one",
    description:
      "API, scheduler, and job execution in a single process. The simplest setup — good for small services, internal tools, or getting started. One deployable, one connection string, done.",
    blocks: [
      {
        label: "Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
        .AddScheduler(scheduler => scheduler
            .Schedule<IProcessOrderTrain>(...)
        )
);

builder.AddTraxDashboard();
builder.Services.AddTraxGraphQL();`,
      },
    ],
  },
  {
    name: "API + Scheduler",
    description:
      "The API handles requests and queues work. A separate scheduler process picks up jobs and runs them locally. Good when you want to isolate heavy processing from your request path — the API stays fast, the scheduler does the lifting.",
    blocks: [
      {
        label: "Api / Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
);
builder.Services.AddTraxGraphQL();`,
      },
      {
        label: "Scheduler / Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
        .AddScheduler(scheduler => scheduler
            .Schedule<IProcessOrderTrain>(...)
        )
);
builder.AddTraxDashboard();`,
      },
    ],
  },
  {
    name: "Hub + Workers",
    description:
      "A hub manages scheduling and the API. Stateless worker nodes poll the database for jobs and execute them independently. Scale workers horizontally — add more when load increases, remove them when it drops. Workers coordinate through PostgreSQL row locking, no message broker required.",
    blocks: [
      {
        label: "Hub / Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
        .AddScheduler(scheduler => scheduler
            .Schedule<IProcessOrderTrain>(...)
        )
);
builder.Services.AddTraxGraphQL();`,
      },
      {
        label: "Worker / Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
);
builder.Services.AddTraxWorker(opts => {
    opts.WorkerCount = 4;
    opts.PollingInterval = TimeSpan.FromSeconds(1);
});`,
      },
    ],
  },
  {
    name: "Ephemeral dispatch",
    description:
      "The API dispatches work over HTTP to a stateless runner — think Lambda functions or short-lived containers. No persistent worker process, no polling. The runner boots, executes one pipeline, and exits. Good for bursty workloads where you don't want idle compute.",
    blocks: [
      {
        label: "Api / Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
        .AddScheduler(scheduler => scheduler
            .UseRemoteWorkers(
                remote => remote.BaseUrl = "https://runner/trax/execute",
                routing => routing
                    .ForTrain<IProcessOrderTrain>()
            )
            .UseRemoteRun(
                remote => remote.BaseUrl = "https://runner/trax/run"
            )
        )
);`,
      },
      {
        label: "Runner / Function.cs",
        code: `public class Function : TraxLambdaFunction
{
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTrax(trax =>
            trax.AddEffects(effects =>
                    effects.UsePostgres(conn))
                .AddMediator(assembly)
        );
    }
}`,
      },
    ],
  },
  {
    name: "Just the scheduler",
    description:
      "No API at all. A standalone process that runs pipelines on a schedule — data processing, ETL, report generation, background maintenance. If you don't need an external trigger, you don't need an API.",
    blocks: [
      {
        label: "Program.cs",
        code: `builder.Services.AddTrax(trax =>
    trax.AddEffects(effects => effects.UsePostgres(conn))
        .AddMediator(assembly)
        .AddScheduler(scheduler => scheduler
            .Schedule<IDataProcessingTrain>(
                "data-processing",
                new DataProcessingInput(),
                Every.Minutes(5)
            )
            .ThenInclude<IReportingTrain>(...)
        )
);
builder.AddTraxDashboard();`,
      },
    ],
  },
];

async function HighlightedBlock({ block }: { block: CodeBlock }) {
  const html = await codeToHtml(block.code, {
    lang: "csharp",
    theme: "github-dark-dimmed",
  });

  return (
    <div className="overflow-x-auto rounded border border-border/50">
      <div className="border-b border-border/50 bg-bg-secondary px-4 py-2">
        <span className="font-mono text-xs text-text-muted">{block.label}</span>
      </div>
      <div
        className="[&_pre]:bg-bg-secondary [&_pre]:p-4 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed"
        dangerouslySetInnerHTML={{ __html: html }}
      />
    </div>
  );
}

export default async function DeploymentArchitectures() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Deploy it however you want.
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Same pipelines, different topologies. Move from a single process to
          distributed workers to serverless dispatch by changing a few lines of
          configuration — the pipeline code stays the same.
        </p>

        <div className="mt-12 space-y-16">
          {architectures.map((arch, i) => (
            <div key={arch.name}>
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-xs text-text-muted">
                  {String(i + 1).padStart(2, "0")}
                </span>
                <h3 className="text-lg font-semibold text-text-primary">
                  {arch.name}
                </h3>
              </div>
              <p className="mt-2 max-w-xl text-sm leading-relaxed text-text-secondary">
                {arch.description}
              </p>
              <div className="mt-4 space-y-3">
                {arch.blocks.map((block) => (
                  <HighlightedBlock key={block.label} block={block} />
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
