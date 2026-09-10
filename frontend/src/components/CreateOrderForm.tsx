import { useEffect, useState } from "react";
import { ApiError, createOrder, getStock } from "../api/client";
import { ApiValidationError, type Product } from "../api/types";
import { ErrorAlert } from "./ErrorAlert";

interface FieldErrors {
  clienteNombre?: string;
  sku?: string;
  quantity?: string;
}

const MIN_QUANTITY = 1;
const MAX_QUANTITY = 100;

function validate(clienteNombre: string, sku: string, quantity: string): FieldErrors {
  const errors: FieldErrors = {};

  if (!clienteNombre.trim()) {
    errors.clienteNombre = "El nombre del cliente es obligatorio.";
  }

  if (!sku) {
    errors.sku = "Selecciona un SKU del catálogo.";
  }

  if (!quantity.trim() || !Number.isInteger(Number(quantity))) {
    errors.quantity = "Ingresa una cantidad entera.";
  } else {
    const qty = Number(quantity);
    if (qty < MIN_QUANTITY || qty > MAX_QUANTITY) {
      errors.quantity = `La cantidad debe estar entre ${MIN_QUANTITY} y ${MAX_QUANTITY}.`;
    }
  }

  return errors;
}

export function CreateOrderForm({ onOrderCreated }: { onOrderCreated: () => void }) {
  const [products, setProducts] = useState<Product[]>([]);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [loadingCatalog, setLoadingCatalog] = useState(true);

  const [clienteNombre, setClienteNombre] = useState("");
  const [sku, setSku] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [touched, setTouched] = useState<Record<keyof FieldErrors, boolean>>({
    clienteNombre: false,
    sku: false,
    quantity: false,
  });

  const [submitting, setSubmitting] = useState(false);
  const [apiErrors, setApiErrors] = useState<string[]>([]);
  const [generalError, setGeneralError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const errors = validate(clienteNombre, sku, quantity);
  const isValid = Object.keys(errors).length === 0;

  const loadCatalog = () => {
    setLoadingCatalog(true);
    setCatalogError(null);
    getStock()
      .then((data) => setProducts(data))
      .catch((error) => {
        const message =
          error instanceof ApiError || error instanceof Error
            ? error.message
            : "No se pudo cargar el catálogo de productos.";
        setCatalogError(message);
      })
      .finally(() => setLoadingCatalog(false));
  };

  useEffect(loadCatalog, []);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setTouched({ clienteNombre: true, sku: true, quantity: true });
    setApiErrors([]);
    setGeneralError(null);
    setSuccessMessage(null);

    if (!isValid) return;

    setSubmitting(true);
    try {
      const order = await createOrder({
        clienteNombre: clienteNombre.trim(),
        items: [{ sku, quantity: Number(quantity) }],
      });
      setSuccessMessage(
        `Pedido creado para ${order.clienteNombre} (SKU ${sku} × ${quantity}). Estado inicial: Pendiente.`,
      );
      setClienteNombre("");
      setSku("");
      setQuantity("1");
      setTouched({ clienteNombre: false, sku: false, quantity: false });
      onOrderCreated();
    } catch (error) {
      if (error instanceof ApiValidationError) {
        setApiErrors(error.errors);
      } else if (error instanceof ApiError || error instanceof Error) {
        setGeneralError(error.message);
      } else {
        setGeneralError("Ocurrió un error inesperado al crear el pedido.");
      }
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <section className="card">
      <h2>Crear pedido</h2>

      <ErrorAlert title="No se pudo crear el pedido:" messages={apiErrors} />
      {generalError && <ErrorAlert title="Error:" messages={[generalError]} />}
      {successMessage && (
        <div className="alert alert-success" role="status">
          {successMessage}
        </div>
      )}

      <form onSubmit={handleSubmit} noValidate>
        <div className="field">
          <label htmlFor="clienteNombre">Nombre del cliente</label>
          <input
            id="clienteNombre"
            type="text"
            value={clienteNombre}
            onChange={(e) => setClienteNombre(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, clienteNombre: true }))}
            aria-invalid={touched.clienteNombre && !!errors.clienteNombre}
            placeholder="Ej. Juan Pérez"
          />
          {touched.clienteNombre && errors.clienteNombre && (
            <p className="field-error">{errors.clienteNombre}</p>
          )}
        </div>

        <div className="field">
          <label htmlFor="sku">Producto (SKU)</label>
          {catalogError ? (
            <div className="catalog-error">
              <span>{catalogError}</span>
              <button type="button" onClick={loadCatalog} className="link-button">
                Reintentar
              </button>
            </div>
          ) : (
            <select
              id="sku"
              value={sku}
              onChange={(e) => setSku(e.target.value)}
              onBlur={() => setTouched((t) => ({ ...t, sku: true }))}
              aria-invalid={touched.sku && !!errors.sku}
              disabled={loadingCatalog}
            >
              <option value="">
                {loadingCatalog ? "Cargando catálogo..." : "Selecciona un SKU"}
              </option>
              {products.map((product) => (
                <option key={product.sku} value={product.sku}>
                  {product.sku} — {product.name} (stock: {product.stockAvailable})
                </option>
              ))}
            </select>
          )}
          {touched.sku && errors.sku && <p className="field-error">{errors.sku}</p>}
        </div>

        <div className="field">
          <label htmlFor="quantity">Cantidad</label>
          <input
            id="quantity"
            type="number"
            min={MIN_QUANTITY}
            max={MAX_QUANTITY}
            value={quantity}
            onChange={(e) => setQuantity(e.target.value)}
            onBlur={() => setTouched((t) => ({ ...t, quantity: true }))}
            aria-invalid={touched.quantity && !!errors.quantity}
          />
          {touched.quantity && errors.quantity && <p className="field-error">{errors.quantity}</p>}
        </div>

        <button type="submit" disabled={submitting}>
          {submitting ? "Creando..." : "Crear pedido"}
        </button>
      </form>
    </section>
  );
}
