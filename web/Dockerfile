# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM node:24-alpine AS deps
WORKDIR /app
ENV COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable && corepack prepare pnpm@11.5.2 --activate
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

FROM deps AS build
WORKDIR /app
COPY . .
ENV NEXT_TELEMETRY_DISABLED=1
RUN pnpm build

FROM node:24-alpine AS run
ARG RELEASE_SHA=unknown
WORKDIR /app
RUN apk add --no-cache tzdata wget \
    && addgroup -S swapmid \
    && adduser -S swapmid -G swapmid \
    && mkdir -p /app/var/next-static-archive \
    && chown -R swapmid:swapmid /app

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    SESSION_STORE_MODE=redis \
    TZ=UTC \
    PORT=3000

COPY --from=build --chown=swapmid:swapmid /app/.next/standalone ./
COPY --from=build --chown=swapmid:swapmid /app/.next/static ./.next/static
COPY --from=build --chown=swapmid:swapmid /app/public ./public
COPY --from=build --chown=swapmid:swapmid /app/scripts/*.mjs ./scripts/
COPY --from=build --chown=swapmid:swapmid /app/src/lib/*.mjs ./src/lib/

LABEL org.opencontainers.image.title="SwapMid Web" \
      org.opencontainers.image.revision="$RELEASE_SHA" \
      org.swapmid.session-refresh-protocol="2"

USER swapmid
EXPOSE 3000
CMD ["node", "scripts/start-production.mjs"]
