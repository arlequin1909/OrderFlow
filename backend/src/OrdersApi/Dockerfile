# Build context is backend/ (see docker-compose.yml) so this can reach the sibling
# OrderFlow.Shared project referenced by OrdersApi.csproj.

# ---- build stage ----
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy only the .csproj files first so `dotnet restore` is cached as its own Docker layer —
# it only reruns when a project file actually changes, not on every source edit.
COPY src/OrdersApi/OrdersApi.csproj src/OrdersApi/
COPY src/OrderFlow.Shared/OrderFlow.Shared.csproj src/OrderFlow.Shared/
RUN dotnet restore src/OrdersApi/OrdersApi.csproj

COPY src/OrdersApi/ src/OrdersApi/
COPY src/OrderFlow.Shared/ src/OrderFlow.Shared/
RUN dotnet publish src/OrdersApi/OrdersApi.csproj -c Release -o /app/publish --no-restore

# ---- runtime stage ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
RUN addgroup -S orderflow && adduser -S orderflow -G orderflow
COPY --from=build /app/publish .
USER orderflow

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OrdersApi.dll"]
