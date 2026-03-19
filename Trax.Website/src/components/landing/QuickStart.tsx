import Link from "next/link";

export default function QuickStart() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Import an existing API.
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          OpenAPI or GraphQL. The CLI reads the schema, generates typed trains
          and junctions, wires up the hub. You fill in the business logic.
        </p>

        <div className="mt-8 max-w-2xl">
          <p className="text-sm text-text-muted">From an OpenAPI spec:</p>
          <div className="mt-3 border-l-2 border-accent/30 bg-bg-secondary py-4 pl-5 pr-5 font-mono text-sm">
            <div className="text-text-muted">
              $ dotnet tool install -g Trax.Cli
            </div>
            <div className="mt-1 text-text-primary">
              $ trax generate --schema ./payments-api.json --output ./Payments
              --name Payments
            </div>
          </div>

          <p className="mt-8 text-sm text-text-muted">Or a GraphQL schema:</p>
          <div className="mt-3 border-l-2 border-accent/30 bg-bg-secondary py-4 pl-5 pr-5 font-mono text-sm text-text-primary">
            $ trax generate --schema ./schema.graphql --output ./Payments --name
            Payments
          </div>

          <p className="mt-8 text-sm text-text-muted">What comes out:</p>
          <pre className="mt-3 overflow-x-auto border-l-2 border-border bg-bg-secondary py-4 pl-5 pr-5 font-mono text-[13px] leading-relaxed text-text-secondary">
            {`Payments/
├── Payments.Hub/              `}
            <span className="text-text-muted">API + Scheduler + Dashboard</span>
            {`
│   └── Program.cs
└── Payments.Trains/
    ├── Models/
    │   └── Transaction.cs
    └── Trains/
        └── Transactions/
            ├── ListTransactions/
            │   ├── IListTransactionsTrain.cs       `}
            <span className="text-accent">[TraxQuery]</span>
            {`
            │   ├── ListTransactionsTrain.cs
            │   └── Junctions/
            │       └── ListTransactionsJunction.cs  `}
            <span className="text-text-muted">← your code here</span>
            {`
            └── ChargeCard/
                ├── IChargeCardTrain.cs              `}
            <span className="text-accent">[TraxMutation]</span>
            {`
                ├── ChargeCardTrain.cs
                └── Junctions/
                    └── ChargeCardJunction.cs        `}
            <span className="text-text-muted">← your code here</span>
          </pre>

          <p className="mt-6 text-sm text-text-muted">
            Fill in the junctions, run the hub.
          </p>

          <div className="mt-8">
            <Link
              href="/docs/getting-started"
              className="text-sm font-medium text-accent hover:text-accent-hover"
            >
              Read the getting started guide &rarr;
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
