import { codeToHtml } from "shiki";

const problemCode = `var inventory = await _inventory.CheckAsync(request.Items);
if (!inventory.Available)
    return Error("Items out of stock");

var payment = await _payments.ChargeAsync(request.PaymentMethod, request.Total);
if (!payment.Success)
    return Error("Payment failed");

var shipment = await _shipping.CreateAsync(request.Address, request.Items);
if (shipment == null)
    return Error("Shipping setup failed");

return new OrderReceipt(payment, shipment);`;

const solutionCode = `protected override async Task<Either<Exception, OrderReceipt>>
    RunInternal(OrderRequest input)
    => Activate(input)
        .Chain<CheckInventoryStep>()
        .Chain<ChargePaymentStep>()
        .Chain<CreateShipmentStep>()
        .Resolve();`;

export default async function ProblemSolution() {
  const [problemHtml, solutionHtml] = await Promise.all([
    codeToHtml(problemCode, { lang: "csharp", theme: "github-dark-dimmed" }),
    codeToHtml(solutionCode, { lang: "csharp", theme: "github-dark-dimmed" }),
  ]);

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        {/* The problem — full width, not in a card */}
        <div>
          <p className="font-mono text-xs uppercase tracking-widest text-derail">
            The problem
          </p>
          <h2 className="mt-3 max-w-lg text-2xl font-semibold text-text-primary">
            Error handling buries the thing you actually care about.
          </h2>
          <p className="mt-3 max-w-lg text-text-secondary">
            Three operations, three if-statements, three early returns. The
            &ldquo;what we do&rdquo; disappears behind &ldquo;what might go
            wrong.&rdquo;
          </p>
        </div>

        <div
          className="mt-8 overflow-x-auto border-l-2 border-derail/40 [&_pre]:bg-bg-secondary [&_pre]:py-5 [&_pre]:pl-6 [&_pre]:pr-6 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-[1.7]"
          dangerouslySetInnerHTML={{ __html: problemHtml }}
        />

        {/* The fix — offset to the right */}
        <div className="mt-20 lg:ml-auto lg:max-w-2xl">
          <p className="font-mono text-xs uppercase tracking-widest text-accent">
            With Trax
          </p>
          <h2 className="mt-3 text-2xl font-semibold text-text-primary">
            Same logic. Read it top to bottom.
          </h2>
          <p className="mt-3 text-text-secondary">
            Each step&apos;s output feeds the next. Errors short-circuit
            automatically — no if-statements, no try-catch, no early returns.
            What&apos;s left is just the business logic, in order.
          </p>
        </div>

        <div
          className="mt-8 overflow-x-auto border-l-2 border-accent/40 lg:ml-auto lg:max-w-2xl [&_pre]:bg-bg-secondary [&_pre]:py-5 [&_pre]:pl-6 [&_pre]:pr-6 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-[1.7]"
          dangerouslySetInnerHTML={{ __html: solutionHtml }}
        />

        <p className="mt-6 text-sm text-text-muted lg:ml-auto lg:max-w-2xl">
          Each step&apos;s output is stored in Memory by type. The next step
          declares what it needs as its input, and Trax wires them together
          automatically. A compile-time analyzer catches broken chains before
          you ever run the code.
        </p>
      </div>
    </section>
  );
}
