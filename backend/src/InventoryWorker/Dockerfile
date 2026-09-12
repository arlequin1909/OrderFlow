# Build context is backend/ (see docker-compose.yml).

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy only the .csproj file first so `dotnet restore` is cached as its own Docker layer —
# it only reruns when the project file actually changes, not on every source edit.
COPY src/InventoryWorker/InventoryWorker.csproj src/InventoryWorker/
RUN dotnet restore src/InventoryWorker/InventoryWorker.csproj

COPY src/InventoryWorker/ src/InventoryWorker/
RUN dotnet publish src/InventoryWorker/InventoryWorker.csproj -c Release -o /app/publish --no-restore

# ---- runtime stage ----
# Plain "runtime" image, not "aspnet" — InventoryWorker is a pure background service with no
# HTTP surface at all (see README "Architecture decisions"), so it needs no web hosting layer.
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime
WORKDIR /app
RUN addgroup -S orderflow && adduser -S orderflow -G orderflow
COPY --from=build /app/publish .
USER orderflow

ENTRYPOINT ["dotnet", "InventoryWorker.dll"]
