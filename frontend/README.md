# frontend

SPA de React + TypeScript (Vite) para la tienda en línea. Consume `OrdersApi` directamente
desde el navegador (InventoryWorker es un background service sin superficie HTTP — ver el
README de la raíz).

## Vistas

- **Crear pedido**: formulario con `clienteNombre`, un `<select>` de SKU (poblado desde
  `GET /api/catalog` de OrdersApi) y `cantidad`. Valida en tiempo real en el cliente (nombre
  no vacío, SKU seleccionado, cantidad entre 1 y 100 — mismas reglas que
  `CreateOrderRequestValidator`) y muestra los errores de validación del servidor (`400` como
  un `ValidationProblemDetails` estándar) en una alerta visible en pantalla, no solo en consola.
- **Pedidos recientes**: tabla con cliente, SKU × cantidad, estado (`Pending`/`Confirmed`/
  `Rejected`) y motivo de rechazo cuando aplica. Hace polling a `GET /api/orders` cada
  `VITE_POLL_INTERVAL_MS` (4s por defecto) para reflejar automáticamente el cambio de estado
  que produce InventoryWorker vía RabbitMQ, sin recargar la página. También se refresca al
  instante justo después de crear un pedido.

## Correr en desarrollo

Requiere `OrdersApi` corriendo (ver el README de la raíz) — por defecto en
`http://localhost:5081`, con CORS habilitado para el origen de este dev server
(`http://localhost:5173` por defecto; ver `Cors:AllowedOrigins` en OrdersApi). Para ver el
flujo completo (confirmación/rechazo de pedidos) `InventoryWorker` también debe estar
corriendo, aunque el frontend no le habla directamente — solo a OrdersApi.

```bash
cd frontend
cp .env.example .env   # opcional, solo si OrdersApi corre en otra URL/puerto
npm install
npm run dev
```

## Build de producción

```bash
npm run build   # genera dist/
npm run preview # sirve el build localmente para revisarlo
```

## Variables de entorno

Ver [`.env.example`](.env.example): `VITE_ORDERS_API_URL`, `VITE_POLL_INTERVAL_MS`.
