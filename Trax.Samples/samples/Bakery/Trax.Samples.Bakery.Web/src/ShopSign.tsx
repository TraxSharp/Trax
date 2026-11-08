import { readShopSignView } from "./trax/index.g";
import { usePoll } from "./usePoll";

/** The shop sign view: whether the shop is open, read from the system's shop, which every customer and baker reads. */
export function ShopSign() {
  const { value, error } = usePoll(readShopSignView);
  const label = value?.shop?.open;
  const open = label === "We're open";
  return (
    <section aria-label="Shop sign" className={`sign${open ? " open" : ""}`}>
      <span className="sign-light" aria-hidden />
      <div className="sign-text">
        <div className="sign-label">The shop</div>
        <div className="sign-value" data-shop-sign>
          {label ?? "…"}
        </div>
      </div>
      <p className="sign-note">
        {error ??
          (open
            ? "A baker clocked in. It closes on its own after hours: a timer in the feature, not a cron job."
            : "Nobody can open it by hand. It opens when a baker clocks in, a policy the features describe.")}
      </p>
    </section>
  );
}
