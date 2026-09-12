# ---- build stage ----
FROM node:20-alpine AS build
WORKDIR /app

# Cache npm install as its own layer — only reruns when package*.json actually changes.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# Vite inlines VITE_* variables into the static bundle at build time (browsers can't read
# container env vars at runtime), so they must be supplied as build args. This defaults to
# the port docker-compose publishes OrdersApi on — override via docker-compose.yml
# `build.args` (or --build-arg) if that port changes. InventoryWorker has no URL of its own:
# it's a pure background service with no HTTP surface (see README "Architecture decisions").
ARG VITE_ORDERS_API_URL=http://localhost:5081
ARG VITE_POLL_INTERVAL_MS=4000
ENV VITE_ORDERS_API_URL=$VITE_ORDERS_API_URL \
    VITE_POLL_INTERVAL_MS=$VITE_POLL_INTERVAL_MS

RUN npm run build

# ---- runtime stage ----
FROM nginx:1.27-alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
