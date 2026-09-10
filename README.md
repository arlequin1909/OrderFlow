# OrderFlow

Monorepo de la prueba técnica de tienda en línea.

```
backend/    Solución .NET 8 (OrdersApi, InventoryWorker, OrderFlow.Shared)
frontend/   Aplicación cliente (pendiente de scaffold)
docker-compose.yml   Infra local: PostgreSQL + RabbitMQ
.env.example          Variables de entorno de referencia
```

## Backend

- **OrdersApi**: recibe órdenes (`POST /api/orders`), las persiste en Postgres y publica un
  evento `order-created` en RabbitMQ.
- **InventoryWorker**: expone el stock (`GET /api/stock`), aplica el seed inicial de
  productos al arrancar, y consume `order-created` para descontar stock.
- **OrderFlow.Shared**: contratos de eventos, opciones de RabbitMQ y `NebulaSyncHelper`
  (estampado de correlationId/timestamp y validación de esquema compartida entre ambos
  servicios).

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
