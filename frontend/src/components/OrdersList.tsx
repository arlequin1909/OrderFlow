import { useCallback, useEffect, useState } from "react";
import { getOrders } from "../api/client";
import type { Order } from "../api/types";
import { ErrorAlert } from "./ErrorAlert";
import { StatusBadge } from "./StatusBadge";

const POLL_INTERVAL_MS = Number(import.meta.env.VITE_POLL_INTERVAL_MS ?? "4000");

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}

/** refreshSignal: bump this from the parent (e.g. right after creating an order) to refetch
 *  immediately instead of waiting for the next poll tick. */
export function OrdersList({ refreshSignal }: { refreshSignal: number }) {
  const [orders, setOrders] = useState<Order[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  const fetchOrders = useCallback(() => {
    getOrders()
      .then((data) => {
        setOrders(data);
        setError(null);
        setLastUpdated(new Date());
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : "No se pudieron cargar los pedidos.");
      })
      .finally(() => setLoading(false));
  }, []);

  // Refetch on mount and whenever the parent bumps refreshSignal (e.g. after creating an order).
  useEffect(() => {
    fetchOrders();
  }, [fetchOrders, refreshSignal]);

  // Periodic polling so status changes (Pending -> Confirmed/Rejected) show up automatically.
  useEffect(() => {
    const interval = setInterval(fetchOrders, POLL_INTERVAL_MS);
    return () => clearInterval(interval);
  }, [fetchOrders]);

  return (
    <section className="card">
      <div className="card-header">
        <h2>Pedidos recientes</h2>
        <div className="poll-status">
          {lastUpdated && <span>Actualizado: {lastUpdated.toLocaleTimeString()}</span>}
          <button type="button" className="link-button" onClick={fetchOrders}>
            Actualizar ahora
          </button>
        </div>
      </div>

      {error && <ErrorAlert title="No se pudieron cargar los pedidos:" messages={[error]} />}

      {loading ? (
        <p className="muted">Cargando pedidos...</p>
      ) : orders.length === 0 ? (
        <p className="muted">Todavía no hay pedidos.</p>
      ) : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Cliente</th>
                <th>SKU × cantidad</th>
                <th>Estado</th>
                <th>Detalle</th>
                <th>Creado</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((order) => (
                <tr key={order.id}>
                  <td>{order.clienteNombre}</td>
                  <td>
                    {order.items.map((item) => (
                      <div key={item.id}>
                        {item.sku} × {item.quantity}
                      </div>
                    ))}
                  </td>
                  <td>
                    <StatusBadge status={order.status} />
                  </td>
                  <td className="muted">
                    {order.status === "Rejected" && order.rejectionReason}
                    {order.status === "Pending" && !order.eventPublished && (
                      <span title={order.eventPublishError ?? undefined}>
                        Reintentando notificar al broker…
                      </span>
                    )}
                  </td>
                  <td className="muted">{formatDate(order.createdAtUtc)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
