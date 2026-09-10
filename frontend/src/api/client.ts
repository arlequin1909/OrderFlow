import { ApiValidationError, type CreateOrderRequest, type Order, type Product } from "./types";

// Base URLs for each backend service, overridable via .env (see .env.example).
// Defaults match the ports used when running both APIs locally with `dotnet run`.
const ORDERS_API_URL = import.meta.env.VITE_ORDERS_API_URL ?? "http://localhost:5081";
const INVENTORY_API_URL = import.meta.env.VITE_INVENTORY_API_URL ?? "http://localhost:5080";

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
    const errors =
      body && typeof body === "object" && "errors" in body && Array.isArray((body as { errors: unknown }).errors)
        ? ((body as { errors: unknown[] }).errors.map(String))
        : ["Solicitud inválida."];
    throw new ApiValidationError(errors);
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

export function getStock(): Promise<Product[]> {
  return request<Product[]>(`${INVENTORY_API_URL}/api/stock`);
}
