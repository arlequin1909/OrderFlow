# OrderFlow

Prueba técnica de una tienda en línea: un flujo de pedidos orquestado con eventos asíncronos.
`OrdersApi` recibe y valida pedidos, los publica en RabbitMQ; `InventoryWorker` reserva stock
de forma idempotente y responde el resultado; `OrdersApi` escucha esa respuesta y confirma o
rechaza el pedido. Un frontend en React lo expone con creación de pedidos y una lista en vivo.

```
backend/    Solución .NET 8: OrdersApi, InventoryWorker, OrderFlow.Shared, OrderFlow.Tests
frontend/   SPA React + TypeScript (Vite) — crear pedidos y verlos con su estado en vivo
docker-compose.yml   Levanta el sistema completo: Postgres, RabbitMQ, ambas APIs y el frontend
.env.example          Variables de entorno de referencia para docker-compose
```

## Levantar todo en menos de 10 minutos

Requisito único: Docker Desktop (o Docker Engine + Compose) corriendo.

```bash
git clone <este-repo> && cd OrderFlow
docker compose up --build -d
```

Eso es todo — sin pasos manuales. Un solo comando:
1. Levanta PostgreSQL y RabbitMQ, y espera a que ambos reporten `healthy`.
2. Construye las imágenes de `OrdersApi` e `InventoryWorker` (multi-stage: SDK de .NET para
   compilar, runtime ASP.NET sobre Alpine para ejecutar) y las arranca — cada una aplica sus
   migraciones de EF Core y, en el caso de `InventoryWorker`, carga el stock inicial
   automáticamente al arrancar.
3. Construye el frontend (Node para el build de Vite, nginx Alpine para servirlo) y lo arranca.

En una máquina con la imagen base ya cacheada esto toma 1–3 minutos; en frío (primera vez,
descargando imágenes base) normalmente no supera los 10.

Verificar que todo quedó arriba:

```bash
docker compose ps   # los 5 servicios deben verse "Up" (postgres/rabbitmq además "healthy")
```

Abrir **http://localhost:5173** — ya se puede crear un pedido y verlo pasar de `Pendiente` a
`Confirmado`/`Rechazado` en la tabla, sin recargar la página.

| Servicio          | URL local                          |
|-------------------|-------------------------------------|
| Frontend           | http://localhost:5173              |
| OrdersApi          | http://localhost:5081/swagger      |
| InventoryWorker    | http://localhost:5080/swagger      |
| RabbitMQ (admin)   | http://localhost:15672 (user/pass en `.env.example`) |
| PostgreSQL         | localhost:5432                     |

**Swagger (OpenAPI):** cada API expone su documentación interactiva en `/swagger` —
[http://localhost:5081/swagger](http://localhost:5081/swagger) para OrdersApi y
[http://localhost:5080/swagger](http://localhost:5080/swagger) para InventoryWorker. Queda
habilitado siempre (incluso corriendo dentro de Docker con `ASPNETCORE_ENVIRONMENT=Production`,
que es el valor por defecto en `docker-compose.yml`), justamente para que se pueda probar cada
endpoint (`POST/GET /api/orders`, `GET /api/stock`) desde el navegador sin necesitar Postman
ni tocar código.

Apagar todo (y borrar los datos, para un arranque 100% limpio la próxima vez):

```bash
docker compose down -v
```

## Correr los tests (un solo comando)

```bash
dotnet test backend/OrderFlow.sln
```

No requiere Docker, Postgres ni RabbitMQ corriendo: los tests usan SQLite en memoria y un
publisher de eventos falso (ver "Tests" más abajo).

## Arquitectura

```
                 ┌────────────┐        order-created        ┌──────────────────┐
  Browser  ───▶  │  OrdersApi │ ───────────────────────────▶ │  InventoryWorker  │
 (frontend)      │            │                               │                   │
                 │ Postgres:  │ ◀─────────────────────────── │  Postgres:        │
                 │ orders,    │   stock-reserved /            │  stock,           │
                 │ order_items│   stock-rejected              │  stock_reservations│
                 └────────────┘                               └──────────────────┘
                        │                                              │
                        └──────────────── RabbitMQ ────────────────────┘
```

- **OrdersApi**: `POST/GET /api/orders`. Valida la entrada, persiste el pedido como `Pending`,
  publica `order-created`, y consume `stock-reserved`/`stock-rejected` para mover el pedido a
  `Confirmed`/`Rejected`.
- **InventoryWorker**: `GET /api/stock`. Carga el stock inicial al arrancar, consume
  `order-created` de forma idempotente y transaccional para reservar (o rechazar) stock, y
  publica el resultado.
- **OrderFlow.Shared**: contratos de eventos, opciones/nombres de colas de RabbitMQ,
  `RabbitMqConsumerBase` (reconexión robusta compartida por ambos consumers) y
  `NebulaSyncHelper` (estampado de `EventId`/`CorrelationId`/timestamp y validación de esquema
  compartida entre ambos servicios).
- **frontend**: SPA de React que consume ambas APIs directamente desde el navegador.

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

## Decisiones de arquitectura y trade-offs

Estas son las decisiones de diseño más discutibles del proyecto, junto con la razón detrás de
cada una y lo que se sacrificó a cambio — para que quien lo revise no las lea como
casualidades.

- **Comunicación asíncrona (RabbitMQ) entre OrdersApi e InventoryWorker, en vez de HTTP
  síncrono.** Un pedido se acepta aunque InventoryWorker esté caído en ese instante; el
  trade-off es consistencia eventual (el cliente ve `Pending` hasta que llega la respuesta) y
  más piezas móviles (colas, idempotencia, reconciliación) que un simple `POST` síncrono.
- **Una sola instancia de Postgres compartida por ambos servicios** (en vez de una base de
  datos por servicio, lo "correcto" en microservicios estrictos). Simplifica el
  `docker-compose` y permite que `OrdersApi` valide la existencia de un SKU leyendo
  directamente la tabla `stock` de InventoryWorker (`CatalogDbContext`) sin una llamada HTTP
  adicional. El costo es acoplamiento de esquema entre servicios — documentado explícitamente
  en el código como algo a resolver con una base por servicio (o esa validación vía HTTP) si
  esto creciera a un sistema real. Cada servicio sí migra su propio esquema con su propia
  tabla de historial de migraciones (`__EFMigrationsHistory_orders` /
  `__EFMigrationsHistory_inventory`) para no pisarse entre sí al arrancar en paralelo.
- **Ledger de idempotencia propio (`stock_reservations`) en vez de deduplicación del broker.**
  RabbitMQ no garantiza entrega exactly-once; en vez de depender de configuración del broker,
  cada orden procesada deja una fila con índice único en `OrderId`, verificada antes de tocar
  stock. Es una fila más por orden y una consulta extra por mensaje, a cambio de una garantía
  explícita y testeada (ver sección Tests) en vez de implícita.
- **"Outbox" simplificado (flags `EventPublished`/`ResponseEventPublished` en vez de un patrón
  outbox transaccional completo con job de reintento).** Cuando el broker no está disponible
  al publicar, la escritura en base de datos ya ocurrió y no se revierte; solo se marca que la
  notificación quedó pendiente. Es la mitad del patrón outbox (el estado durable) sin la otra
  mitad (el worker que reintenta solo); ver "Qué haría distinto con más tiempo".
- **Reserva de stock atómica y todo-o-nada por pedido**, no por línea de producto. Si un pedido
  tiene 3 SKUs y a uno le falta stock, se rechaza el pedido completo en vez de reservar
  parcialmente — más simple de razonar para el usuario ("tu pedido se confirmó o no") a costa
  de rechazar pedidos que técnicamente podrían cumplirse parcialmente.
- **El frontend llama a ambas APIs directamente desde el navegador** (sin un backend-for-frontend
  ni API Gateway). Para una SPA de este tamaño evita una capa adicional; el costo es que el
  navegador necesita conocer dos URLs base y ambas APIs necesitan CORS configurado
  explícitamente para el origen del frontend.
- **Vite incrusta las URLs de las APIs en el bundle en tiempo de build** (`ARG`/`ENV` en el
  Dockerfile del frontend), no en tiempo de ejecución. Es la forma estándar de Vite y evita
  necesitar un servidor de configuración; el costo es que la imagen de frontend queda atada a
  las URLs con las que se compiló — cambiarlas implica reconstruir la imagen, no solo cambiar
  una variable de entorno del contenedor en producción.
- **Contenedores individuales con `docker-compose` en un solo host**, apropiado para esta
  entrega y para desarrollo local, en vez de una plataforma de orquestación más robusta
  (Kubernetes) o una infraestructura basada en hiperconvergencia (cómputo + almacenamiento +
  virtualización administrados como una sola capa, tipo VMware vSAN/Nutanix) pensada para
  producción con alta disponibilidad real. Ver "Qué haría distinto con más tiempo".

## Backend

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

### Endpoint de InventoryWorker

| Método | Ruta               | Descripción                          |
|--------|--------------------|----------------------------------------|
| GET    | `/api/stock`       | Lista el stock de todos los productos. |
| GET    | `/api/stock/{sku}` | Stock de un SKU puntual.               |

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
   contenedor de RabbitMQ en medio de la prueba, incluso contra el stack dockerizado completo.

### Reserva de stock en InventoryWorker (idempotencia y errores robustos)

`StockReservationService` (ver
[`StockReservationService.cs`](backend/src/InventoryWorker/Services/StockReservationService.cs))
contiene toda la lógica crítica, deliberadamente separada de `OrderCreatedConsumer` (que solo
maneja la mecánica de RabbitMQ) para que sea testeable de forma directa — ver "Tests".

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
Probado dos veces: con un test automatizado (ver "Tests") y publicando manualmente el mismo
evento (mismo `OrderId`/`EventId`) dos veces contra la cola `order-created` en el sistema real
— el stock bajó una sola vez y el segundo intento quedó registrado en el log como omitido por
idempotencia.

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
  Probado deteniendo el contenedor de RabbitMQ antes de arrancar InventoryWorker, y
  observado también "en salvaje" al levantar `docker compose up`: RabbitMQ reportó `healthy`
  un instante antes de aceptar conexiones AMQP, y ambos consumers absorbieron ese margen con
  su propio retry sin caerse.
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

## Tests

```bash
dotnet test backend/OrderFlow.sln
```

5 tests (xUnit) en [`backend/tests/OrderFlow.Tests`](backend/tests/OrderFlow.Tests), sobre la
lógica crítica pedida — validaciones, transición de estados e idempotencia del consumidor —
corriendo contra EF Core con SQLite en memoria (no un mock de `DbContext`: consultas, índices
únicos y transacciones reales) y un publisher de RabbitMQ falso, sin necesitar Docker:

| Archivo | Qué cubre |
|---|---|
| [`OrdersControllerTests.cs`](backend/tests/OrderFlow.Tests/OrdersApi/OrdersControllerTests.cs) | `Create_WithInvalidInput_ReturnsBadRequestWithExpectedError` (Theory: `clienteNombre` vacío, SKU inexistente, cantidad fuera de 1–100) y `Create_WithValidInput_PersistsOrderAsPendingAndPublishesEvent`. |
| [`StockReservationServiceTests.cs`](backend/tests/OrderFlow.Tests/InventoryWorker/StockReservationServiceTests.cs) | `ReserveAsync_WithSufficientStock_DecrementsStockAndReturnsReserved`, `ReserveAsync_WithInsufficientStock_RejectsWithoutChangingStock`, y `ReserveAsync_CalledTwiceForSameOrderId_IsIdempotent` (la orden se "reentrega" con el mismo `OrderId`/`EventId` y se verifica que el stock se descuenta una sola vez). |

La lógica de idempotencia/reserva vive en `StockReservationService`, separada a propósito de
`OrderCreatedConsumer` (que solo maneja RabbitMQ) — así el test la ejercita directamente en
vez de tener que levantar un broker.

## Docker

Cada servicio de aplicación tiene un `Dockerfile` multi-stage (compilar con el SDK, correr con
el runtime — la imagen final no lleva el SDK ni el código fuente):

- [`backend/src/OrdersApi/Dockerfile`](backend/src/OrdersApi/Dockerfile) y
  [`backend/src/InventoryWorker/Dockerfile`](backend/src/InventoryWorker/Dockerfile): SDK de
  .NET 8 sobre Alpine para `dotnet publish`, runtime ASP.NET 8 sobre Alpine para ejecutar,
  usuario no-root, capas de `dotnet restore` cacheadas por separado del código fuente.
- [`frontend/Dockerfile`](frontend/Dockerfile): Node 20 Alpine para `npm run build`, nginx
  Alpine para servir el resultado estático. Las URLs de las APIs se inyectan como build args
  (ver trade-off arriba).

`docker-compose.yml` en la raíz orquesta los 5 servicios con `depends_on: condition:
service_healthy` sobre Postgres/RabbitMQ, así que `ordersapi`/`inventoryworker` nunca arrancan
antes de que la infraestructura esté realmente lista — sin eso, sus migraciones fallarían al
arrancar.

## Variables de entorno

Todas documentadas con valores de ejemplo en [`.env.example`](.env.example) (raíz, para
`docker-compose`) y [`frontend/.env.example`](frontend/.env.example) (para `npm run dev`
fuera de Docker). Nunca hay credenciales ni cadenas de conexión hardcodeadas en el código:

| Variable | Usada por | Propósito |
|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | postgres, ordersapi, inventoryworker | Credenciales y nombre de la base. |
| `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS` | rabbitmq, ordersapi, inventoryworker | Credenciales del broker. |
| `ConnectionStrings__Postgres` | ordersapi, inventoryworker | Cadena de conexión completa (compuesta en `docker-compose.yml` a partir de las de arriba). |
| `RabbitMq__HostName/Port/UserName/Password/VirtualHost` | ordersapi, inventoryworker | Conexión al broker. |
| `Cors__AllowedOrigins` | ordersapi, inventoryworker | Origen permitido para el frontend. |
| `ORDERS_API_PORT` / `INVENTORY_API_PORT` / `FRONTEND_PORT` | docker-compose | Puertos publicados en el host. |
| `VITE_ORDERS_API_URL` / `VITE_INVENTORY_API_URL` / `VITE_POLL_INTERVAL_MS` | frontend | URLs de las APIs y frecuencia de polling (incrustadas en el bundle al compilar). |

## Frontend

SPA en React + TypeScript (Vite) con dos secciones: crear pedidos (validación en tiempo real
+ errores del servidor visibles en pantalla) y una tabla de pedidos recientes con polling
automático del estado. Detalle completo en [`frontend/README.md`](frontend/README.md).

Para correrlo fuera de Docker (con `OrdersApi`/`InventoryWorker` corriendo por separado):

```bash
cd frontend
npm install
npm run dev
```

## Qué haría distinto con más tiempo

- **Outbox transaccional real**, no la versión simplificada actual. Un worker en background
  que escanee periódicamente `Orders`/`StockReservations` con la notificación pendiente
  (`EventPublished`/`ResponseEventPublished` en `false`) y reintente publicarla con backoff
  (ej. Polly), en vez de depender de la siguiente entrega del mismo mensaje para reintentar.
- **Una base de datos por servicio de verdad**, con la validación de SKU en `OrdersApi` hecha
  vía HTTP a `InventoryWorker` (con su propio timeout/circuit breaker) en vez de leer
  directamente la tabla `stock` — la separación real que un sistema de microservicios en
  producción debería tener, en vez del atajo documentado arriba.
- **Configuración del frontend en tiempo de ejecución**, no de build: servir un
  `env.js`/`config.json` desde nginx (o usar `envsubst` sobre un template al arrancar el
  contenedor) para que la misma imagen de frontend sirva para varios entornos sin
  reconstruirla.
- **Trazabilidad end-to-end real**: ya existe `CorrelationId` en cada evento, pero falta
  logging estructurado (ej. Serilog + Seq) y tracing distribuido (OpenTelemetry) que lo
  aproveche para seguir un pedido a través de los tres saltos (OrdersApi → RabbitMQ →
  InventoryWorker → RabbitMQ → OrdersApi) en una sola vista.
- **Más cobertura de tests**: tests de integración con Testcontainers (Postgres + RabbitMQ
  reales, no SQLite/fake) para los `BackgroundService` completos, y un smoke test end-to-end
  del frontend (Playwright) corriendo en CI contra el stack dockerizado.
- **CI/CD**: un pipeline (GitHub Actions) que corra `dotnet test`, el build del frontend, y
  `docker compose build` en cada PR, y que publique las imágenes a un registry.
- **Autenticación/autorización** — hoy no existe; cualquiera que alcance las APIs puede crear
  pedidos o ver el catálogo completo.
- **Healthchecks propios en las APIs de .NET** (`/health`) para que `docker-compose` (u
  orquestadores más sofisticados) puedan verificar su salud real, no solo la de Postgres/RabbitMQ.
- **Repensar la topología de despliegue para producción**: este `docker-compose` es correcto
  para desarrollo local y para esta entrega, pero para producción evaluaría Kubernetes (con
  autoscaling y rolling updates reales) o, si la organización ya invirtió en ello, desplegar
  sobre infraestructura basada en hiperconvergencia (HCI) — cómputo, almacenamiento y
  virtualización administrados como una sola capa (VMware vSAN, Nutanix, Azure Stack HCI) — en vez de
  contenedores sueltos en un único host, para tener alta disponibilidad real de Postgres/RabbitMQ
  en vez de un solo contenedor de cada uno.
