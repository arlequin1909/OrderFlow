export type OrderStatus = "Pending" | "Confirmed" | "Rejected";

export interface OrderItem {
  id: number;
  orderId: string;
  sku: string;
  quantity: number;
}

export interface Order {
  id: string;
  clienteNombre: string;
  status: OrderStatus;
  eventPublished: boolean;
  eventPublishError: string | null;
  rejectionReason: string | null;
  createdAtUtc: string;
  items: OrderItem[];
}

export interface CreateOrderRequest {
  clienteNombre: string;
  items: { sku: string; quantity: number }[];
}

export interface Product {
  id: number;
  sku: string;
  name: string;
  stockAvailable: number;
  updatedAtUtc: string;
}

/** Thrown when OrdersApi responds 400 with { errors: string[] } — the client-facing validation errors. */
export class ApiValidationError extends Error {
  errors: string[];

  constructor(errors: string[]) {
    super(errors.join(" "));
    this.name = "ApiValidationError";
    this.errors = errors;
  }
}
