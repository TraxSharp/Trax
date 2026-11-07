import { readShopSignView } from "./trax/index.g";
import { usePoll } from "./usePoll";

/** The shop sign view: whether the shop is open, read from the system's shop, which every customer and baker reads. */
export function ShopSign() {
  const { value, error } = usePoll(readShopSignView);
  return (
    <section aria-label="Shop sign" className="card">
      <h2>The shop</h2>
      <p className="sign" data-shop-sign>
        {value?.shop?.open ?? "…"}
      </p>
      {error && <p className="problem">{error}</p>}
    </section>
  );
}
