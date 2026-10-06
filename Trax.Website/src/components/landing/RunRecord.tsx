import Link from "next/link";

const fields: { name: string; value: string; tone?: "fail" }[] = [
  {
    name: "Name",
    value:
      "Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard.IRecalculateLeaderboardTrain",
  },
  { name: "TrainState", value: "Failed", tone: "fail" },
  { name: "StartTime", value: "2026-09-29 03:15:00.412" },
  { name: "EndTime", value: "2026-09-29 03:15:02.087" },
  { name: "Input", value: '{ "region": "na" }' },
  { name: "FailureJunction", value: "RankPlayersJunction", tone: "fail" },
  { name: "FailureException", value: "TimeoutException", tone: "fail" },
  { name: "FailureReason", value: "Score service did not answer within 2s" },
  { name: "ManifestId", value: "14  (leaderboard-na)" },
  { name: "HostName", value: "worker-2" },
];

export default function RunRecord() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto grid max-w-6xl gap-10 px-6 sm:px-8 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)] lg:gap-16">
        <div>
          <h2 className="text-2xl font-semibold text-text-primary">
            Every run leaves a record
          </h2>
          <p className="mt-3 text-text-secondary">
            API calls, background jobs and cron schedules all go through the
            same pipeline, and each run gets a row in Postgres: when it
            started, how it ended, which junction failed and the exception it
            threw. Turn on parameter saving and the input and output are stored
            with it.
          </p>
          <p className="mt-4 text-text-secondary">
            When a job fails overnight, you open the run and read what
            happened. The dashboard shows the same rows, and so does a SQL
            query.
          </p>
          <Link
            href="/docs/effect/metadata"
            className="mt-6 inline-block text-sm font-medium text-accent hover:text-accent-hover"
          >
            What gets recorded &rarr;
          </Link>
        </div>

        <div className="min-w-0">
          <div className="rounded border border-border/50 bg-bg-secondary">
            <div className="border-b border-border/50 px-4 py-2">
              <span className="font-mono text-xs text-text-muted">
                trax.metadata, id 88213
              </span>
            </div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 p-4 font-mono text-[13px]">
              {fields.map((f) => (
                <div key={f.name} className="contents">
                  <dt className="text-text-muted">{f.name}</dt>
                  <dd
                    className={`min-w-0 [overflow-wrap:anywhere] ${
                      f.tone === "fail" ? "text-derail" : "text-text-primary"
                    }`}
                  >
                    {f.value}
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        </div>
      </div>
    </section>
  );
}
