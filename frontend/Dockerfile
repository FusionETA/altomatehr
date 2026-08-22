# syntax=docker/dockerfile:1

# ---- build the SPA ----
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . ./
# same-origin API path — the SPA calls /api/*, Caddy strips /api and proxies to the backend
ARG VITE_API_URL=/api
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build

# ---- edge: TLS + serve SPA + proxy /api ----
FROM caddy:2-alpine AS runtime
COPY --from=build /app/dist /srv
COPY Caddyfile /etc/caddy/Caddyfile
