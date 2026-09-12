import { ApiValidationError, type CreateOrderRequest, type Order, type Product } from "./types";

// Base URL for OrdersApi, overridable via .env (see .env.example). Defaults to the port used
// when running it locally with `dotnet run`. InventoryWorker has no HTTP surface of its own
// (it's a pure background service) — the product catalog is served by OrdersApi instead.
const ORDERS_API_URL = import.meta.env.VITE_ORDERS_API_URL ?? "http://localhost:5081";

async function parseJsonSafely(response: Response): Promise<unknown> {
  const text = await response.text();
  if (!text) return null;
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

/** Generic "the request failed and it wasn't a 400 validation error" case (network down, 500, etc.). */
export class ApiError extends Error {}

/**
 * The backend returns validation failures as a standard RFC 7807 ValidationProblemDetails:
 * `{ type, title, status, errors: { fieldName: string[] } }`. This flattens every field's
 * messages into a single list for display — the exact field key doesn't matter to the user.
 */
function extractValidationErrors(body: unknown): string[] {
  if (
    body &&
    typeof body === "object" &&
    "errors" in body &&
    body.errors &&
    typeof body.errors === "object"
  ) {
    const messages = Object.values(body.errors as Record<string, unknown>)
      .flatMap((value) => (Array.isArray(value) ? value : [value]))
      .map(String);
    if (messages.length > 0) return messages;
  }

  return ["Solicitud inválida."];
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(url, {
      headers: { "Content-Type": "application/json" },
      ...init,
    });
  } catch {
    throw new ApiError(
      "No se pudo conectar con el servidor. Verifica que la API esté corriendo y vuelve a intentar.",
    );
  }

  if (response.status === 400) {
    const body = await parseJsonSafely(response);
    throw new ApiValidationError(extractValidationErrors(body));
  }

  if (!response.ok) {
    throw new ApiError(`El servidor respondió con un error inesperado (HTTP ${response.status}).`);
  }

  const body = await parseJsonSafely(response);
  return body as T;
}

export function getOrders(): Promise<Order[]> {
  return request<Order[]>(`${ORDERS_API_URL}/api/orders`);
}

export function createOrder(payload: CreateOrderRequest): Promise<Order> {
  return request<Order>(`${ORDERS_API_URL}/api/orders`, {
    method: "POST",
    body: JSON.stringify(payload),
  });
}

export function getCatalog(): Promise<Product[]> {
  return request<Product[]>(`${ORDERS_API_URL}/api/catalog`);
}
