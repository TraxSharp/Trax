import { useEffect, useState } from "react";
import { myOrders, type MyOrdersResult } from "./trax/index.g";

type Order = NonNullable<NonNullable<NonNullable<MyOrdersResult>["nodes"]>[number]>;

export function MyOrders() {
  const [orders, setOrders] = useState<Order[]>([]);

  useEffect(() => {
    myOrders().then((page) => setOrders(page?.nodes?.filter((o) => o !== null) ?? []));
  }, []);

  return (
    <section>
      <h2>Your orders</h2>
      <ul>
        {orders.map((o) => (
          <li key={o.id}>
            {new Date(o.placedAt).toLocaleString()}: {o.total}
          </li>
        ))}
      </ul>
    </section>
  );
}
