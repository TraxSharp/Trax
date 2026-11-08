import { readShopSignView } from "./trax/index.g";
import { usePoll } from "./usePoll";

/** The shop sign view: whether the shop is open, read from the system's shop, which every customer and baker reads. */
export function ShopSign() {
  const { value, error } = usePoll(readShopSignView);
  const label = value?.shop?.open;
  const open = label?.startsWith("We're open") ?? false;
  const firing = label === "We're open, the oven is on";
  return (
    <section aria-label="Shop sign" className={`sign${open ? " open" : ""}${firing ? " firing" : ""}`}>
      <span className="sign-light" aria-hidden />
      <div className="sign-text">
        <div className="sign-label">The shop</div>
        <div className="sign-value" data-shop-sign>
          {label ?? "…"}
        </div>
      </div>
      <p className="sign-note">
        {error ??
          (firing
            ? "A baker fired the oven: every waiting order just went in."
            : open
              ? "A baker is in. Paid orders wait until they fire the oven."
              : "Closed until a baker clocks in. Nobody opens it by hand: it is the system's.")}
      </p>
    </section>
  );
}
