#!/usr/bin/env bash
# SwapMid local development launcher (Linux / macOS / WSL2).
# Usage:
#   ./scripts/dev.sh                # Start infra + migrate + launch API (:44362) & Web (:5175)
#   ./scripts/dev.sh --infra-only   # Start infra + migrate and exit
#   ./scripts/dev.sh --skip-infra   # Reuse already-running local services

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${REPO_ROOT}/docker/.env"
ENV_EXAMPLE="${REPO_ROOT}/docker/.env.example"
COMPOSE_FILE="${REPO_ROOT}/docker/docker-compose.yml"
COMPOSE_PROJECT="${COMPOSE_PROJECT:-swapmid-dev}"

INFRA_ONLY=0
SKIP_INFRA=0
SKIP_MIGRATE=0

for arg in "$@"; do
  case "$arg" in
    --infra-only) INFRA_ONLY=1 ;;
    --skip-infra) SKIP_INFRA=1 ;;
    --skip-migrate) SKIP_MIGRATE=1 ;;
    *)
      echo "Unknown option: $arg" >&2
      exit 1
      ;;
  esac
done

if [[ ! -f "$ENV_FILE" ]]; then
  cp "$ENV_EXAMPLE" "$ENV_FILE"
  echo "[dev] Created docker/.env from docker/.env.example"
fi

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

PG_PORT="${POSTGRES_PORT:-15432}"
PG_DB="${POSTGRES_DB:-swapmid}"
PG_USER="${POSTGRES_USER:-swapmid}"
PG_PASS="${POSTGRES_PASSWORD:-SwapMid_ChangeMe_Postgres!2026}"
REDIS_HOST_PORT="${REDIS_PORT:-16379}"
RMQ_PORT="${RABBITMQ_AMQP_PORT:-15672}"
RMQ_USER="${RABBITMQ_USER:-swapmid}"
RMQ_PASS="${RABBITMQ_PASSWORD:-SwapMid_ChangeMe_RabbitMQ!2026}"
SEQ_HOST_PORT="${SEQ_PORT:-15341}"

if [[ "$SKIP_INFRA" -eq 0 ]]; then
  echo "[dev] Starting backing services via Docker Compose (${COMPOSE_PROJECT})..."
  docker compose -p "$COMPOSE_PROJECT" -f "$COMPOSE_FILE" --env-file "$ENV_FILE" up -d --wait postgres redis rabbitmq seq
fi

export ConnectionStrings__Default="Host=127.0.0.1;Port=${PG_PORT};Database=${PG_DB};User ID=${PG_USER};Password=${PG_PASS};SSL Mode=Disable;GSS Encryption Mode=Disable;"
export Redis__Configuration="127.0.0.1:${REDIS_HOST_PORT}"
export RabbitMQ__Host="127.0.0.1"
export RabbitMQ__Port="${RMQ_PORT}"
export RabbitMQ__UserName="${RMQ_USER}"
export RabbitMQ__Password="${RMQ_PASS}"
export Serilog__WriteTo__2__Args__serverUrl="http://127.0.0.1:${SEQ_HOST_PORT}"

if [[ "$SKIP_MIGRATE" -eq 0 ]]; then
  echo "[dev] Running SwapMid.DbMigrator..."
  dotnet run --project "${REPO_ROOT}/src/SwapMid.DbMigrator" --no-launch-profile
fi

if [[ "$INFRA_ONLY" -eq 1 ]]; then
  echo "[dev] Backing infrastructure and database seed are ready."
  exit 0
fi

echo "[dev] Starting API Host (https://localhost:44362) and Web BFF (http://localhost:5175)..."
dotnet run --project "${REPO_ROOT}/src/SwapMid.HttpApi.Host" --launch-profile "SwapMid.HttpApi.Host" &
API_PID=$!

cleanup() {
  if kill -0 "$API_PID" >/dev/null 2>&1; then
    echo "[dev] Stopping SwapMid.HttpApi.Host (PID ${API_PID})..."
    kill "$API_PID" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT INT TERM

cd "${REPO_ROOT}/web"
export INTERNAL_API_URL="https://localhost:44362"
export NEXT_PUBLIC_API_URL="https://localhost:44362"
export SESSION_STORE_MODE="redis"
export REDIS_URL="redis://127.0.0.1:${REDIS_HOST_PORT}/0"
export ALLOW_INSECURE_TLS="true"
pnpm dev
