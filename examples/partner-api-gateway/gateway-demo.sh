#!/usr/bin/env bash
set -euo pipefail

API_BASE="${SWAPMID_API_URL:-http://localhost:18080}"
CLIENT_ID="${SWAPMID_CLIENT_ID:?Set SWAPMID_CLIENT_ID}"
CLIENT_SECRET="${SWAPMID_CLIENT_SECRET:?Set SWAPMID_CLIENT_SECRET}"

TOKEN="$(curl -fsS -X POST "${API_BASE}/connect/token" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "grant_type=client_credentials" \
  --data-urlencode "client_id=${CLIENT_ID}" \
  --data-urlencode "client_secret=${CLIENT_SECRET}" \
  --data-urlencode "scope=external-integration" | jq -r '.access_token')"

echo "1) Calling managed gateway route..."
curl -fsS "${API_BASE}/api/gateway/gw/demo/health" \
  -H "Authorization: Bearer ${TOKEN}"
echo ""

echo "2) Publishing idempotent partner event into MessageBus..."
curl -fsS -X POST "${API_BASE}/api/gateway/events/integration.pipeline.events" \
  -H "Authorization: Bearer ${TOKEN}" \
  -H "Idempotency-Key: partner-order-10001-v1" \
  -H "X-SwapMid-Event-Subject: partner.orders.created" \
  -H "Content-Type: application/json" \
  -d '{"orderId":"ORD-10001","status":"Created"}'
echo ""
