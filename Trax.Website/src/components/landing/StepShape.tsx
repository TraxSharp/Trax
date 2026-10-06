import { codeToHtml } from "shiki";

const chainCode = `protected override Task<Either<Exception, OrderReceipt>> Junctions() =>
    Chain<CheckInventoryJunction>()
        .Chain<ChargePaymentJunction>()
        .Chain<CreateShipmentJunction>()
        .Resolve();`;

const inlineCode = `var inventory = await _inventory.CheckAsync(request.Items);
if (!inventory.Available)
    return Error("Items out of stock");

var payment = await _payments.ChargeAsync(request.Payment, request.Total);
if (!payment.Success)
    return Error("Payment failed");

var shipment = await _shipping.CreateAsync(request.Address, request.Items);
if (shipment == null)
    return Error("Shipping setup failed");

return new OrderReceipt(payment, shipment);`;

function Panel({
  file,
  note,
  html,
  accent,
}: {
  file: string;
  note?: string;
  html: string;
  accent?: boolean;
}) {
  return (
    <div
      className={`overflow-hidden rounded border ${
        accent ? "border-accent/30" : "border-border/50"
      }`}
    >
      <div className="flex items-baseline justify-between gap-4 border-b border-border/50 bg-bg-secondary px-4 py-2">
        <span className="font-mono text-xs text-text-secondary">{file}</span>
        {note && <span className="text-xs text-text-muted">{note}</span>}
      </div>
      <div
        className="[&_pre]:overflow-x-auto [&_pre]:bg-bg-secondary [&_pre]:p-4 [&_pre]:font-mono [&_pre]:text-[12.5px] [&_pre]:leading-relaxed"
        dangerouslySetInnerHTML={{ __html: html }}
      />
    </div>
  );
}

export default async function StepShape() {
  const [chainHtml, inlineHtml] = await Promise.all([
    codeToHtml(chainCode, { lang: "csharp", theme: "github-dark-dimmed" }),
    codeToHtml(inlineCode, { lang: "csharp", theme: "github-dark-dimmed" }),
  ]);

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto grid max-w-6xl gap-10 px-6 sm:px-8 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)] lg:gap-14">
        <div className="space-y-4 text-text-secondary">
          <h2 className="text-2xl font-semibold text-text-primary">
            Each step does one thing. Trax handles the space between them.
          </h2>
          <p>
            A junction takes a typed input and returns a typed output. When one
            throws, the rest are skipped and the train arrives carrying the
            exception, so the route reads top to bottom. Written by hand, every
            call needs its own check, and the order of operations gets lost in
            the error handling.
          </p>
          <p>
            Each junction&apos;s output is stored in Memory by type, and the
            next junction declares what it needs. You never pass values between
            steps by hand.
          </p>
          <p>
            The rest of Trax is built on this structure. Because every step is
            declared before the train runs, the host checks the whole chain at
            startup, the run record names the exact junction that failed, and
            the train can be published as a typed API without a controller.
          </p>
        </div>

        <div className="min-w-0 space-y-4">
          <Panel file="ProcessOrderTrain.cs" html={chainHtml} accent />
          <Panel
            file="OrderService.cs"
            note="the same route without Trax"
            html={inlineHtml}
          />
        </div>
      </div>
    </section>
  );
}
