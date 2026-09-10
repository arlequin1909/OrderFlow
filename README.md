# OrderFlow

Monorepo de la prueba técnica de tienda en línea.

```
backend/    Solución .NET 8 (OrdersApi, InventoryWorker, OrderFlow.Shared)
frontend/   Aplicación cliente (pendiente de scaffold)
docker-compose.yml   Infra local: PostgreSQL + RabbitMQ
.env.example          Variables de entorno de referencia
```

## Backend

- **OrdersApi**: gestiona órdenes y publica un evento `order-created` en RabbitMQ.
- **InventoryWorker**: expone el stock (`GET /api/stock`), aplica el seed inicial de
  productos al arrancar, y consume `order-created` para descontar stock.
- **OrderFlow.Shared**: contratos de eventos, opciones de RabbitMQ y `NebulaSyncHelper`
  (estampado de correlationId/timestamp y validación de esquema compartida entre ambos
  servicios).

### Endpoints de OrdersApi

| Método | Ruta                | Descripción                                                              |
|--------|---------------------|---------------------------------------------------------------------------|
| POST   | `/api/orders`       | Crea una orden. Valida entrada, persiste como `Pending` y publica el evento. |
| GET    | `/api/orders`       | Lista todas las órdenes con su estado.                                    |
| GET    | `/api/orders/{id}`  | Detalle de una orden.                                                     |

**Validación de `POST /api/orders`** (400 Bad Request con `{ "errors": [...] }` si falla alguna):
- `clienteNombre`: no puede estar vacío.
- `items[].sku`: debe existir en el catálogo de stock (tabla `stock`, propiedad de
  InventoryWorker — ver [`CatalogDbContext`](backend/src/OrdersApi/Data/CatalogDbContext.cs)
  para el trade-off de esta validación).
- `items[].quantity`: entero entre 1 y 100.

Si la validación pasa, la orden se guarda con `Status = Pending` y se intenta publicar el
evento `OrderCreated`. Ver la sección siguiente para qué pasa si esa publicación falla.

### Manejo de fallos del broker (RabbitMQ no disponible al publicar)

El flujo de `POST /api/orders` separa explícitamente "persistir la orden" de "publicar el
evento", y un fallo en el segundo paso **no revierte ni bloquea** el primero:

1. La orden ya se guardó en Postgres con `Status = Pending` **antes** de intentar publicar
   — esa escritura no depende del broker.
2. `RabbitMqPublisher.TryPublishOrderCreated` (ver
   [`RabbitMqPublisher.cs`](backend/src/OrdersApi/Messaging/RabbitMqPublisher.cs)) nunca
   lanza una excepción por problemas de conectividad: atrapa `BrokerUnreachableException`,
   `SocketException`, `AlreadyClosedException`, `TimeoutException` y
   `OperationInterruptedException`, registra el error con `ILogger`, y devuelve `false` +
   un mensaje.
3. La orden se actualiza con `EventPublished = false` y `EventPublishError = "<motivo>"`, y
   el endpoint igual responde **201 Created** — el pedido fue aceptado y persistido
   correctamente; una caída transitoria del broker no debe impedir tomar pedidos.
4. Consecuencia visible: mientras `EventPublished` sea `false`, InventoryWorker nunca recibió
   el evento y **no descontó stock** para esa orden. Esto se puede detectar consultando
   `GET /api/orders` y filtrando por `eventPublished: false`.
5. Resiliencia de conexión: el publisher no conecta a RabbitMQ en su constructor (evita que
   una caída del broker impida siquiera resolver el controller o levantar la app). La
   conexión/canal se crean de forma perezosa en el primer intento de publicar
   (`AutomaticRecoveryEnabled = true`, reconexión automática cada 5s), y se recrean si se
   encuentran cerrados en el siguiente intento — probado apagando y reiniciando el
   contenedor de RabbitMQ en medio de la prueba.

**Limitación conocida / siguiente paso (no implementado en esta iteración):** no hay
reintento automático en background para las órdenes con `EventPublished = false`. En
producción se recomienda:
- Un job/worker que reintente periódicamente esas órdenes (patrón *transactional outbox*
  usando los campos `EventPublished`/`EventPublishError` ya existentes).
- Política de reintento con backoff (ej. Polly) alrededor de `BasicPublish`.
- Alerta si hay órdenes `Pending` con `EventPublished = false` por más de X minutos.

### Seed automático

Al arrancar, `InventoryWorker` aplica las migraciones de EF Core y carga el stock inicial si
la tabla `stock` está vacía:

| SKU     | Stock inicial |
|---------|---------------|
| ABC-01  | 100           |
| DEF-02  | 50            |
| GHI-03  | 75            |

Ver [`InventorySeeder`](backend/src/InventoryWorker/Seed/InventorySeeder.cs).

### Levantar la infraestructura

```bash
cp .env.example .env
docker compose up -d
```

Esto levanta PostgreSQL (puerto 5432) y RabbitMQ (puerto 5672, UI de administración en
http://localhost:15672).

### Correr las APIs

```bash
cd backend
dotnet run --project src/OrdersApi
dotnet run --project src/InventoryWorker
```

Cada proyecto lee su cadena de conexión y credenciales del broker desde variables de
entorno (`ConnectionStrings__Postgres`, `RabbitMq__HostName`, `RabbitMq__UserName`,
`RabbitMq__Password`, etc.), con valores de conveniencia para desarrollo local en
`appsettings.Development.json`. En producción/CI estas variables deben inyectarse desde el
entorno (o `.env` + docker-compose) y nunca quedar hardcodeadas.

## Frontend

Pendiente de scaffold — ver [`frontend/README.md`](frontend/README.md).
