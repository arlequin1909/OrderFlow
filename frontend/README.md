# frontend

SPA de React + TypeScript (Vite) para la tienda en línea. Consume `OrdersApi` e
`InventoryWorker` directamente desde el navegador.

## Vistas

- **Crear pedido**: formulario con `clienteNombre`, un `<select>` de SKU (poblado desde
  `GET /api/stock` de InventoryWorker) y `cantidad`. Valida en tiempo real en el cliente
  (nombre no vacío, SKU seleccionado, cantidad entre 1 y 100 — mismas reglas que
  `OrdersController`) y muestra los errores de validación del servidor (`400` con
  `{ "errors": [...] }`) en una alerta visible en pantalla, no solo en consola.
- **Pedidos recientes**: tabla con cliente, SKU × cantidad, estado (`Pending`/`Confirmed`/
  `Rejected`) y motivo de rechazo cuando aplica. Hace polling a `GET /api/orders` cada
  `VITE_POLL_INTERVAL_MS` (4s por defecto) para reflejar automáticamente el cambio de estado
  que hace InventoryWorker vía RabbitMQ, sin recargar la página. También se refresca al
  instante justo después de crear un pedido.

## Correr en desarrollo

Requiere `OrdersApi` e `InventoryWorker` corriendo (ver el README de la raíz) — por defecto
en `http://localhost:5081` y `http://localhost:5080`. Ambos deben tener habilitado CORS para
el origen de este dev server (`http://localhost:5173` por defecto; ver `Cors:AllowedOrigins`
en cada API).

```bash
cd frontend
cp .env.example .env   # opcional, solo si las APIs corren en otras URLs/puerto
npm install
npm run dev
```

## Build de producción

```bash
npm run build   # genera dist/
npm run preview # sirve el build localmente para revisarlo
```

## Variables de entorno

Ver [`.env.example`](.env.example): `VITE_ORDERS_API_URL`, `VITE_INVENTORY_API_URL`,
`VITE_POLL_INTERVAL_MS`.
