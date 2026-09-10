import type { OrderStatus } from "../api/types";

const LABELS: Record<OrderStatus, string> = {
  Pending: "Pendiente",
  Confirmed: "Confirmado",
  Rejected: "Rechazado",
};

export function StatusBadge({ status }: { status: OrderStatus }) {
  return <span className={`badge badge-${status.toLowerCase()}`}>{LABELS[status]}</span>;
}
