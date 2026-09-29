import { codeToHtml } from "shiki";

const inlineCode = `var inventory = await _inventory.CheckAsync(request.Items);
if (!inventory.Available)
    return Error("Items out of stock");

var payment = await _payments.ChargeAsync(request.PaymentMethod, request.Total);
if (!payment.Success)
    return Error("Payment failed");

var shipment = await _shipping.CreateAsync(request.Address, request.Items);
if (shipment == null)
    return Error("Shipping setup failed");

return new OrderReceipt(payment, shipment);`;

const chainCode = `protected override Task<Either<Exception, OrderReceipt>> Junctions() =>
    Chain<CheckInventoryJunction>()
        .Chain<ChargePaymentJunction>()
        .Chain<CreateShipmentJunction>()
        .Resolve();`;

export default async function StepShape() {
  const [inlineHtml, chainHtml] = await Promise.all([
    codeToHtml(inlineCode, { lang: "csharp", theme: "github-dark-dimmed" }),
    codeToHtml(chainCode, { lang: "csharp", theme: "github-dark-dimmed" }),
  ]);

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="max-w-2xl text-2xl font-semibold text-text-primary">
          Each step does one thing. Trax handles the space between them.
        </h2>

        <div className="mt-10 grid gap-10 lg:grid-cols-2 lg:gap-12">
          <div>
            <div
              className="overflow-x-auto rounded border border-border/50 [&_pre]:bg-bg-secondary [&_pre]:p-5 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed"
              dangerouslySetInnerHTML={{ __html: inlineHtml }}
            />
            <p className="mt-4 max-w-lg text-sm leading-relaxed text-text-secondary">
              Three calls and three checks. The order of operations is in
              there, but you have to read around the error handling to find
              it.
            </p>
          </div>

          <div>
            <div
              className="overflow-x-auto rounded border border-accent/30 [&_pre]:bg-bg-secondary [&_pre]:p-5 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed"
              dangerouslySetInnerHTML={{ __html: chainHtml }}
            />
            <p className="mt-4 max-w-lg text-sm leading-relaxed text-text-secondary">
              A junction takes a typed input and returns a typed output. When
              one throws, the rest are skipped and the train arrives carrying
              the exception, so the route reads top to bottom.
            </p>
          </div>
        </div>

        <div className="mt-12 max-w-2xl space-y-4 text-text-secondary">
          <p>
            Each junction&apos;s output is stored in Memory by type. The next
            junction declares what it needs as its input, and Trax wires them
            together. You never pass values between steps by hand.
          </p>
          <p>
            The same structure is what the rest of Trax is built on. Because
            every step is declared before the train runs, the host checks the
            whole chain at startup, the run record names the exact junction
            that failed, and the train can be published as a typed API without
            a controller.
          </p>
        </div>
      </div>
    </section>
  );
}
