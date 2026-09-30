import { codeToHtml } from "shiki";
import CodeTabs, { type CodeTab } from "./CodeTabs";

const trainCode = `public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>,
        IRecalculateLeaderboardTrain
{
    protected override Task<Either<Exception, RecalculateLeaderboardOutput>> Junctions() =>
        Chain<AggregateScoresJunction>()
            .Chain<RankPlayersJunction>()
            .Resolve();
}`;

interface RawTab {
  label: string;
  caption: string;
  blocks: { file: string; lang: string; code: string }[];
}

const rawTabs: RawTab[] = [
  {
    label: "Call it",
    caption:
      "Hand the input to the train bus and await the output. The bus finds the train by its input type, runs it in the current request, and records the run.",
    blocks: [
      {
        file: "LeaderboardController.cs",
        lang: "csharp",
        code: `public class LeaderboardController(ITrainBus trains) : ControllerBase
{
    [HttpPost("leaderboard/{region}/recalculate")]
    public Task<RecalculateLeaderboardOutput> Recalculate(string region) =>
        trains.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = region });
}`,
      },
    ],
  },
  {
    label: "Schedule it",
    caption:
      "Give it an id, an input and a schedule. Failed runs are retried with backoff, and a run that keeps failing lands in the dead-letter queue for someone to look at.",
    blocks: [
      {
        file: "Program.cs",
        lang: "csharp",
        code: `.AddScheduler(scheduler => scheduler
    .Schedule<IRecalculateLeaderboardTrain>(
        "leaderboard-na",
        new RecalculateLeaderboardInput { Region = "na" },
        Every.Minutes(5),
        options => options.MaxRetries(3)))`,
      },
    ],
  },
  {
    label: "Serve it",
    caption:
      "Two attributes turn the train into a typed GraphQL mutation. A train exposed this way has to say who may call it, or the host refuses to start.",
    blocks: [
      {
        file: "RecalculateLeaderboardTrain.cs",
        lang: "csharp",
        code: `[TraxMutation(GraphQLOperation.Queue)]
[TraxAuthorize(Roles = "Admin")]
public class RecalculateLeaderboardTrain
    : ServiceTrain<RecalculateLeaderboardInput, RecalculateLeaderboardOutput>,
        IRecalculateLeaderboardTrain`,
      },
      {
        file: "request.graphql",
        lang: "graphql",
        code: `mutation {
  dispatch {
    recalculateLeaderboard(input: { region: "na" }) {
      workQueueId
    }
  }
}`,
      },
    ],
  },
  {
    label: "Move it",
    caption:
      "Start worker processes on other machines against the same database. Each one polls Postgres and claims jobs with FOR UPDATE SKIP LOCKED, so each job is claimed by one worker at a time. Nothing calls the workers over HTTP, and the train class does not change.",
    blocks: [
      {
        file: "Worker / Program.cs",
        lang: "csharp",
        code: `builder.Services.AddTrax(trax => trax
    .AddEffects(effects => effects.UsePostgres(connectionString))
    .AddMediator(typeof(RecalculateLeaderboardTrain).Assembly));

builder.Services.AddTraxWorker(options => options.WorkerCount = 4);`,
      },
    ],
  },
];

export default async function FourWays() {
  const trainHtml = await codeToHtml(trainCode, {
    lang: "csharp",
    theme: "github-dark-dimmed",
  });

  const tabs: CodeTab[] = await Promise.all(
    rawTabs.map(async (tab) => ({
      label: tab.label,
      caption: tab.caption,
      blocks: await Promise.all(
        tab.blocks.map(async (block) => ({
          file: block.file,
          html: await codeToHtml(block.code, {
            lang: block.lang,
            theme: "github-dark-dimmed",
          }),
        })),
      ),
    })),
  );

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          One train, four ways to run it
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          This train recalculates a game&apos;s leaderboard. It is adapted
          from the game server sample, and nothing in it knows how it will be
          run.
        </p>

        <div
          className="mt-8 overflow-hidden rounded border border-accent/30 [&_pre]:overflow-x-auto [&_pre]:bg-bg-secondary [&_pre]:p-5 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed"
          dangerouslySetInnerHTML={{ __html: trainHtml }}
        />

        <div className="mt-10">
          <CodeTabs tabs={tabs} />
        </div>
      </div>
    </section>
  );
}
