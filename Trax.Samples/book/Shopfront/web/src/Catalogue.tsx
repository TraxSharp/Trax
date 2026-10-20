import { useEffect, useState } from "react";
import { catalogue, placeOrder, TraxOperationError, type CatalogueResult } from "./trax/index.g";

// GraphQL lets every level be null, and the generated type says so.
type Product = NonNullable<NonNullable<NonNullable<CatalogueResult>["nodes"]>[number]>;

export function Catalogue({
  onPlaced,
  onAdd,
}: {
  onPlaced: (runId: string) => void;
  // Puts the product in the cart, when the cart can take it.
  onAdd?: (sku: string) => void;
}) {
  const [products, setProducts] = useState<Product[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    catalogue().then((page) => setProducts(page?.nodes?.filter((p) => p !== null) ?? []));
  }, []);

  async function buy(sku: string) {
    setError(null);
    try {
      const placed = await placeOrder({ lines: [{ sku, quantity: 1 }], paymentToken: "tok_visa" });
      if (placed) onPlaced(placed.externalId);
    } catch (e) {
      // A refusal the train meant the customer to read, such as "CAP is out of stock."
      setError(e instanceof TraxOperationError ? e.message : "Something went wrong.");
    }
  }

  return (
    <section>
      <h2>Catalogue</h2>
      <ul>
        {products.map((p) => (
          <li key={p.sku}>
            {p.name}, {p.price} ({p.stock} left){" "}
            <button onClick={() => buy(p.sku)}>Buy one now</button>{" "}
            <button disabled={!onAdd} onClick={() => onAdd?.(p.sku)}>
              Add to cart
            </button>
          </li>
        ))}
      </ul>
      {error && <p role="alert">{error}</p>}
    </section>
  );
}
