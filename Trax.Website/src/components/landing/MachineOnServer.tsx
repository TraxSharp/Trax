import Link from "next/link";
import { codeToHtml } from "shiki";

// Cut from Trax.Samples/samples/Recovery/Trax.Samples.Recovery/Machines/OrderMachine.cs. Keep it in step with the
// sample when the machine changes.
const machineCode = `m.In(OrderState.Cart)
    .On(OrderTrigger.AddItem)
        .AnyAuthenticated()
        .WithInput<NewItem>()
        .When(Input((NewItem i) => i.Item).IsOneOf([.. Menu.Keys]))
        .Because("That is not on the menu.")
        .Reduce(new Reduction.Custom("order:add-item"))
        .To(OrderState.Cart)
    .On(OrderTrigger.PlaceOrder)
        .AnyAuthenticated()
        .When(Field((OrderContext c) => c.Items).CountAtLeast(1))
        .Because("The cart is empty.")
        .To(OrderState.Ordered);

m.In(OrderState.Ordered)
    .Invokes<IDeliverOrderTrain, OrderInput, OrderDelivered>(DeliveryFor)
    .OnDone(OrderState.Delivered)
    .OnFailed(OrderState.Cancelled);`;

const requestCode = `mutation {
  machines { order {
    placeOrder(id: "9f3c…", requestId: "r-41") {
      problem { code message }
    }
  } }
}`;

const answerCode = `{
  "problem": {
    "code": "GUARD_FAILED",
    "message": "The cart is empty."
  }
}`;

const blockClass =
  "[&_pre]:overflow-x-auto [&_pre]:bg-bg-secondary [&_pre]:p-5 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed";

function FileLabel({ name }: { name: string }) {
  return (
    <div className="border-b border-border/50 bg-bg-secondary px-4 py-2">
      <span className="font-mono text-xs text-text-muted">{name}</span>
    </div>
  );
}

export default async function MachineOnServer() {
  const theme = "github-dark-dimmed";
  const [machineHtml, requestHtml, answerHtml] = await Promise.all([
    codeToHtml(machineCode, { lang: "csharp", theme }),
    codeToHtml(requestCode, { lang: "graphql", theme }),
    codeToHtml(answerCode, { lang: "json", theme }),
  ]);

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          The state lives on the server
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          A state machine is written once, in C#. The page runs the same machine to know what it can do next, and
          each move it makes is a mutation the server checks against the machine&apos;s rules and the caller before
          storing anything. A refused move comes back as data, with the reason the machine gives.
        </p>
        <Link
          href="/docs/statemachine/authoring"
          className="mt-4 inline-block text-sm font-medium text-accent hover:text-accent-hover"
        >
          How a machine is written &rarr;
        </Link>

        <div className="mt-8 grid gap-6 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
          <div className={`min-w-0 overflow-hidden rounded border border-accent/30 ${blockClass}`}>
            <FileLabel name="OrderMachine.cs" />
            <div dangerouslySetInnerHTML={{ __html: machineHtml }} />
          </div>

          <div className="flex min-w-0 flex-col gap-6">
            <div className={`overflow-hidden rounded border border-border/50 ${blockClass}`}>
              <FileLabel name="the page sends" />
              <div dangerouslySetInnerHTML={{ __html: requestHtml }} />
            </div>
            <div className={`overflow-hidden rounded border border-border/50 ${blockClass}`}>
              <FileLabel name="the server answers" />
              <div dangerouslySetInnerHTML={{ __html: answerHtml }} />
            </div>
          </div>
        </div>

        <p className="mt-6 max-w-2xl text-sm text-text-muted">
          The page never sends a price. The server reads it from its own menu when an item is added, and only the
          delivery train&apos;s outcome moves an order past <code className="font-mono">Ordered</code>.
        </p>
      </div>
    </section>
  );
}
