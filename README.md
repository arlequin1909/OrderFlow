# OrderFlow

Monorepo de la prueba técnica de tienda en línea.

```
backend/    Solución .NET 8 (OrdersApi, InventoryWorker, OrderFlow.Shared)
frontend/   SPA React + TypeScript (Vite) — crear pedidos y verlos con su estado en vivo
docker-compose.yml   Infra local: PostgreSQL + RabbitMQ
.env.example          Variables de entorno de referencia
```

## Backend

- **OrdersApi**: gestiona órdenes, publica `order-created` en RabbitMQ, y consume
  `stock-reserved`/`stock-rejected` para actualizar el estado del pedido.
- **InventoryWorker**: expone el stock (`GET /api/stock`), aplica el seed inicial de
  productos al arrancar, consume `order-created` de forma idempotente para reservar stock, y
  publica `stock-reserved` o `stock-rejected` con el resultado.
- **OrderFlow.Shared**: contratos de eventos, opciones/colas de RabbitMQ,
  `RabbitMqConsumerBase` (reconexión robusta compartida por ambos consumers) y
  `NebulaSyncHelper` (estampado de EventId/correlationId/timestamp y validación de esquema
  compartida entre ambos servicios).

### Flujo de eventos

```
OrdersApi                    RabbitMQ                    InventoryWorker
   │  POST /api/orders           │                              │
   │  guarda Order (Pending) ────┼── order-created ────────────▶│  idempotencia (atlas-checkpoint)
   │                              │                              │  ├─ stock suficiente → descuenta
   │                              │                              │  │  y publica stock-reserved
   │                              │                              │  └─ stock insuficiente → publica
   │                              │                              │     stock-rejected (nada se descuenta)
   │◀── stock-reserved/rejected ──┼──────────────────────────────┘
   │  Order: Pending → Confirmed/Rejected
```

### Endpoints de OrdersApi

| Método | Ruta                | Descripción                                                              |
|--------|---------------------|---------------------------------------------------------------------------|
| POST   | `/api/orders`       | Crea una orden. Valida entrada, persiste como `Pending` y publica el evento. |
| GET    | `/api/orders`       | Lista todas las órdenes con su estado (`Pending`/`Confirmed`/`Rejected`). |
| GET    | `/api/orders/{id}`  | Detalle de una orden (incluye `rejectionReason` si fue rechazada).        |

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

### Reserva de stock en InventoryWorker (idempotencia y errores robustos)

`OrderCreatedConsumer` (ver
[`OrderCreatedConsumer.cs`](backend/src/InventoryWorker/Messaging/OrderCreatedConsumer.cs))
implementa todo el ciclo de vida de un `OrderCreated`:

**Idempotencia.** Cada orden procesada deja una fila en `stock_reservations`
(`OrderId` con índice único), marcada con el comentario `atlas-checkpoint` en el código. Antes
de tocar stock, se busca si ya existe una reserva para ese `OrderId`:
- Si existe y su notificación ya se publicó → se omite por completo (ni se vuelve a
  descontar stock, ni se vuelve a publicar).
- Si existe pero la notificación quedó pendiente (fallo de broker anterior) → solo se
  reintenta publicar el resultado ya calculado, sin volver a tocar stock.
- Si no existe → se procesa por primera vez.

Como defensa adicional ante una carrera entre dos entregas concurrentes del mismo mensaje, el
índice único de `OrderId` actúa de respaldo: si dos instancias pasan el chequeo anterior a la
vez, solo una logra insertar su `StockReservation` — la otra recibe una violación de
constraint (`23505`), hace rollback de su propio descuento y se retira sin duplicar nada.
Probado publicando manualmente el mismo evento (mismo `OrderId`/`EventId`) dos veces contra la
cola `order-created`: el stock bajó una sola vez y el segundo intento quedó registrado en el
log como omitido por idempotencia.

**Reserva atómica (todo o nada).** Se revisa el stock de *todos* los items de la orden antes
de descontar cualquiera; si algún SKU no existe o no tiene stock suficiente, no se descuenta
nada y la orden completa se marca `Rejected` con el motivo (ej. "Stock insuficiente para el
SKU 'GHI-03' (disponible 75, solicitado 99)."). El chequeo, el descuento y el registro de la
reserva ocurren en una misma transacción de EF Core.

**Manejo de errores robusto** (ver [`RabbitMqConsumerBase.cs`](backend/src/OrderFlow.Shared/Messaging/RabbitMqConsumerBase.cs),
compartida por `OrderCreatedConsumer` y el `StockOutcomeConsumer` de OrdersApi):
- *RabbitMQ caído al arrancar*: la conexión NO se abre en el constructor ni bloquea
  `IHostedService.StartAsync` (si lo hiciera, tumbaría todo el host). Se reintenta con backoff
  creciente (5s, 10s, 15s... hasta 30s) dentro del propio `BackgroundService`, sin afectar el
  resto de la app — `GET /api/stock` sigue respondiendo con normalidad mientras tanto.
  Probado deteniendo el contenedor de RabbitMQ antes de arrancar InventoryWorker.
- *RabbitMQ cae después de conectar*: `AutomaticRecoveryEnabled` de RabbitMQ.Client reconecta
  y vuelve a declarar colas/consumers solo; se loguean las transiciones de
  desconexión/recuperación. Probado deteniendo y reiniciando el contenedor con el consumer ya
  conectado.
- *Mensaje con formato inválido*: se descarta sin reintentar (`nack` sin requeue) — reintentar
  un JSON malformado nunca lo arregla.
- *Falla transitoria de infraestructura* (Postgres caído/timeout al procesar): se espera un
  breve backoff y se hace `nack` con `requeue: true`, para que RabbitMQ reentregue el mensaje
  cuando la base de datos se recupere, en vez de perderlo.
- *Error inesperado*: se trata como permanente (`nack` sin requeue) para evitar un loop
  infinito de reentregas de un mensaje "envenenado"; queda registrado en el log para
  investigar.
- *Fallo al publicar `stock-reserved`/`stock-rejected`* (broker caído en ese momento): igual
  que en OrdersApi, la reserva de stock ya es durable en ese punto — el mensaje `order-created`
  se confirma (`ack`) igualmente, y `StockReservation.ResponseEventPublished = false` +
  `ResponseEventPublishError` quedan registrados para que una próxima entrega del mismo
  `OrderId` (si llegara) reintente solo la publicación. **Limitación conocida**: no hay un job
  de reconciliación en background que reintente proactivamente estas notificaciones pendientes
  — mismo trade-off documentado arriba para `EventPublished` en OrdersApi.

### OrdersApi: de Pending a Confirmed/Rejected

`StockOutcomeConsumer` (ver
[`StockOutcomeConsumer.cs`](backend/src/OrdersApi/Messaging/StockOutcomeConsumer.cs)) escucha
`stock-reserved` y `stock-rejected` y actualiza la orden correspondiente. Solo transiciona
órdenes que siguen en `Pending` — si ya está `Confirmed`/`Rejected` (por una reentrega o un
evento tardío), el evento se ignora y se registra en el log, evitando que un mensaje duplicado
o fuera de orden sobreescriba un estado ya asentado. Usa la misma `RabbitMqConsumerBase` y la
misma política de `nack`/reintento que `OrderCreatedConsumer`.

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

SPA en React + TypeScript (Vite) con dos secciones: crear pedidos (validación en tiempo real
+ errores del servidor visibles en pantalla) y una tabla de pedidos recientes con polling
automático del estado. Requiere que `OrdersApi` e `InventoryWorker` estén corriendo con CORS
habilitado para su origen (`Cors:AllowedOrigins`, por defecto `http://localhost:5173`).
Detalle completo en [`frontend/README.md`](frontend/README.md).

```bash
cd frontend
npm install
npm run dev
```
