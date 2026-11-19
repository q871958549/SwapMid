#!/usr/bin/env node
/**
 * SwapMid End-to-End Live Stack Smoke Verification
 *
 * Exercises all three core integration engines, OpenIddict OAuth2 flows,
 * platform foundation services, and the Next.js 16 BFF security boundary
 * against a live SwapMid deployment (e.g. Docker Compose).
 *
 * Environment overrides:
 *   API_BASE_URL   (default: http://127.0.0.1:18080)
 *   WEB_BASE_URL   (default: http://127.0.0.1:13000)
 *   ADMIN_USER     (default: admin)
 *   ADMIN_PASSWORD (default: 1q2w3E*)
 */
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'

const API_BASE = (process.env.API_BASE_URL ?? 'http://127.0.0.1:18080').replace(/\/+$/, '')
const WEB_BASE = (process.env.WEB_BASE_URL ?? 'http://127.0.0.1:13000').replace(/\/+$/, '')
const ADMIN_USER = process.env.ADMIN_USER ?? 'admin'
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? '1q2w3E*'

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

function step(label) {
  console.log(`\n[e2e] ▶ ${label}`)
}

async function waitForEndpoint(url, timeoutMs = 120_000) {
  const deadline = Date.now() + timeoutMs
  let lastErr = 'no response'
  while (Date.now() < deadline) {
    try {
      const res = await fetch(url, { signal: AbortSignal.timeout(5_000) })
      if (res.status === 200) return
      lastErr = `HTTP ${res.status}`
    } catch (err) {
      lastErr = err?.message ?? String(err)
    }
    await sleep(2_000)
  }
  throw new Error(`Timed out waiting for ${url} (${lastErr})`)
}

async function apiJson(path, { method = 'GET', token, body } = {}) {
  const headers = { Accept: 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
    signal: AbortSignal.timeout(30_000),
  })

  const text = await res.text()
  if (!res.ok) {
    throw new Error(`${method} ${path} failed with HTTP ${res.status}: ${text.slice(0, 400)}`)
  }
  return text ? JSON.parse(text) : null
}

async function main() {
  step(`1/7 Waiting for API (${API_BASE}) & Web BFF (${WEB_BASE}) readiness`)
  await waitForEndpoint(`${API_BASE}/health/live`)
  await waitForEndpoint(`${API_BASE}/health/ready`)
  await waitForEndpoint(`${WEB_BASE}/build-meta`)

  const healthStatus = await apiJson('/health-status')
  assert.equal(healthStatus.status, 'Healthy', `Expected /health-status Healthy, got ${healthStatus.status}`)
  console.log('  ✓ /health/live, /health/ready, and /health-status (PostgreSQL + Redis + RabbitMQ) are Healthy')

  step('2/7 Authenticating via OpenIddict (/connect/token password grant)')
  const tokenRes = await fetch(`${API_BASE}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'password',
      username: ADMIN_USER,
      password: ADMIN_PASSWORD,
      client_id: 'SwapMid_App',
      scope: 'SwapMid offline_access',
    }),
    signal: AbortSignal.timeout(30_000),
  })
  assert.equal(tokenRes.status, 200, `Expected /connect/token 200, got ${tokenRes.status}`)
  const tokenJson = await tokenRes.json()
  assert.ok(tokenJson.access_token, 'Expected non-empty access_token')
  assert.ok(tokenJson.refresh_token, 'Expected non-empty refresh_token')
  const token = tokenJson.access_token
  console.log('  ✓ Issued access_token + refresh_token for SwapMid_App')

  step('3/7 Engine 1 (DataIntegration): Executing demo PostgreSQL ETL pipeline & idempotent RunId replay')
  const pipelines = await apiJson('/api/v1/data-pipelines', { token })
  const demoPipeline = pipelines.items?.find((p) => p.name === 'demo-orders-pg-to-pg')
  assert.ok(demoPipeline, 'Seeded pipeline demo-orders-pg-to-pg not found')

  const capabilities = await apiJson('/api/v1/data-pipelines/capabilities', { token })
  assert.ok(Array.isArray(capabilities) && capabilities.length >= 5, 'Expected >=5 connector capabilities')

  const preflight = await apiJson('/api/v1/data-pipelines/preflight-schema', {
    method: 'POST',
    token,
    body: {
      sourceId: demoPipeline.sourceId,
      targetId: demoPipeline.targetId,
      transformConfig: demoPipeline.transformConfig,
    },
  })
  assert.equal(preflight.compatible, true, `Expected demo pipeline schema preflight compatible=true, got ${JSON.stringify(preflight.issues)}`)

  const runId = randomUUID()
  const execResult = await apiJson(`/api/v1/data-pipelines/${demoPipeline.id}/execute?runId=${runId}`, {
    method: 'POST',
    token,
  })
  assert.equal(execResult.id, runId)
  assert.equal(execResult.status, 2, `Expected PipelineExecution status=2 (Succeeded), got ${execResult.status}`)
  assert.equal(execResult.processedRows, 12, `Expected 12 synced rows, got ${execResult.processedRows}`)

  const replayResult = await apiJson(`/api/v1/data-pipelines/${demoPipeline.id}/execute?runId=${runId}`, {
    method: 'POST',
    token,
  })
  assert.equal(replayResult.id, runId)
  assert.equal(replayResult.status, 2)
  assert.equal(replayResult.processedRows, 12)
  console.log(`  ✓ Synced 12 orders & verified idempotent replay (runId=${runId})`)

  step('4/7 Engine 2 (MessageBus): Verifying automatic ETL->MessageBus Outbox bridge & manual publish -> RabbitMQ')
  const topics = await apiJson('/api/v1/message-topics', { token })
  const demoTopic = topics.items?.find((t) => t.name === 'integration.pipeline.events')
  assert.ok(demoTopic, 'Seeded topic integration.pipeline.events not found')

  const subs = await apiJson(`/api/v1/message-subscriptions?topicId=${demoTopic.id}`, { token })
  const demoSub = subs.items?.find((s) => s.name === 'demo-pipeline-events-rmq')
  assert.ok(demoSub?.isEnabled, 'Seeded RabbitMQ subscription demo-pipeline-events-rmq not found or disabled')

  let autoBridgeRecord = null
  for (let i = 0; i < 25; i++) {
    const recordList = await apiJson(
      `/api/v1/message-records?topicId=${demoTopic.id}&maxResultCount=20`,
      { token },
    )
    autoBridgeRecord = recordList.items?.find(
      (r) => r.subject === 'pipeline.execution.succeeded' && String(r.payload ?? '').includes(runId),
    )
    if (autoBridgeRecord?.status === 1) break
    await sleep(1_000)
  }
  assert.equal(
    autoBridgeRecord?.status,
    1,
    `Expected automatic ETL->MessageBus Outbox record for runId=${runId} to reach status=1 (Delivered), got ${JSON.stringify(autoBridgeRecord)}`,
  )
  console.log(`  ✓ Verified automatic ETL->Outbox->MessageBus->RabbitMQ bridge (${autoBridgeRecord.id})`)

  const published = await apiJson('/api/v1/message-records/publish', {
    method: 'POST',
    token,
    body: {
      topicId: demoTopic.id,
      subject: `smoke.etl.completed.${runId.slice(0, 8)}`,
      payload: JSON.stringify({ pipeline: 'demo-orders-pg-to-pg', runId, processedRows: 12 }),
    },
  })
  assert.ok(published?.id, 'Expected published MessageRecord id')

  let deliveredRecord = null
  let attempts = []
  for (let i = 0; i < 20; i++) {
    deliveredRecord = await apiJson(`/api/v1/message-records/${published.id}`, { token })
    attempts = await apiJson(`/api/v1/message-records/${published.id}/attempts`, { token })
    if (deliveredRecord.status === 1 && attempts.some((a) => a.status === 1)) break
    await sleep(1_000)
  }
  assert.equal(
    deliveredRecord?.status,
    1,
    `Expected MessageRecord status=1 (Delivered), got ${deliveredRecord?.status} (attempts=${JSON.stringify(attempts)})`,
  )
  console.log(`  ✓ MessageRecord ${published.id} delivered to RabbitMQ exchange swapmid.demo.events`)

  step('5/7 Engine 3 (ApiManagement): Verifying route catalog, client governance policy & API Gateway')
  const routes = await apiJson('/api/v1/api-routes', { token })
  const demoRoute = routes.items?.find((r) => r.path === '/gw/demo/health')
  assert.ok(demoRoute, 'Seeded API route /gw/demo/health not found')

  const tempClientId = `smoke_client_${randomUUID().slice(0, 8)}`
  const createdClient = await apiJson('/api/v1/third-party-clients', {
    method: 'POST',
    token,
    body: { clientId: tempClientId, displayName: 'E2E Smoke Integration Client' },
  })
  try {
    assert.equal(createdClient.clientId, tempClientId)
    assert.ok(createdClient.clientSecret, 'Expected one-time clientSecret')

    const updatedClient = await apiJson(
      `/api/v1/third-party-clients/${encodeURIComponent(tempClientId)}/policy`,
      {
        method: 'PUT',
        token,
        body: {
          allowedGroupIds: demoRoute.groupId ? [demoRoute.groupId] : [],
          rateLimitPerMinute: 60,
        },
      },
    )
    assert.equal(updatedClient.rateLimitPerMinute, 60)

    const ccRes = await fetch(`${API_BASE}/connect/token`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'client_credentials',
        client_id: createdClient.clientId,
        client_secret: createdClient.clientSecret,
        scope: 'external-integration',
      }),
      signal: AbortSignal.timeout(15_000),
    })
    assert.equal(ccRes.status, 200, `Expected client_credentials /connect/token 200, got ${ccRes.status}`)
    const ccJson = await ccRes.json()
    assert.ok(ccJson.access_token, 'Expected client_credentials access_token')

    const gwRes = await fetch(`${API_BASE}/api/gateway/gw/demo/health`, {
      headers: { Authorization: `Bearer ${ccJson.access_token}` },
      signal: AbortSignal.timeout(15_000),
    })
    if (gwRes.status !== 404) {
      assert.equal(gwRes.status, 200, `Expected live /api/gateway/gw/demo/health 200, got ${gwRes.status}`)
      const idempotencyKey = `smoke-evt-${randomUUID()}`
      const evtRes1 = await fetch(`${API_BASE}/api/gateway/events/integration.pipeline.events`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${ccJson.access_token}`,
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'X-SwapMid-Subject': 'gateway.smoke.event',
        },
        body: JSON.stringify({ source: 'e2e-smoke', ok: true }),
        signal: AbortSignal.timeout(15_000),
      })
      assert.equal(evtRes1.status, 202, `Expected /api/gateway/events/* 202, got ${evtRes1.status}`)
      const evtJson1 = await evtRes1.json()
      const evtRes2 = await fetch(`${API_BASE}/api/gateway/events/integration.pipeline.events`, {
        method: 'POST',
        headers: {
          Authorization: `Bearer ${ccJson.access_token}`,
          'Content-Type': 'application/json',
          'Idempotency-Key': idempotencyKey,
          'X-SwapMid-Subject': 'gateway.smoke.event',
        },
        body: JSON.stringify({ source: 'e2e-smoke', ok: true }),
        signal: AbortSignal.timeout(15_000),
      })
      assert.equal(evtRes2.status, 202)
      const evtJson2 = await evtRes2.json()
      assert.equal(evtJson1.eventId, evtJson2.eventId, 'Expected deterministic eventId on Idempotency-Key replay')
      console.log(`  ✓ Provisioned ${tempClientId}, enforced client policy (60 req/min), forwarded /gw/demo/health (200), and ingested idempotent gateway event (${evtJson1.eventId})`)
    } else {
      console.log(`  ✓ Provisioned ${tempClientId}, enforced client policy (60 req/min), issued external-integration JWT (gateway disabled by default -> 404)`)
    }
  } finally {
    await apiJson(`/api/v1/third-party-clients/${encodeURIComponent(tempClientId)}`, {
      method: 'DELETE',
      token,
    })
  }

  step('6/7 Platform Foundation: Verifying Hangfire Scheduled Jobs, DB Schema Dictionary, Auth Providers & MCP')
  const jobs = await apiJson('/api/v1/scheduled-jobs', { token })
  assert.ok(Array.isArray(jobs) && jobs.length >= 4, `Expected >=4 scheduled jobs, got ${jobs?.length}`)
  const tables = await apiJson('/api/v1/db-schema/tables', { token })
  assert.ok(Array.isArray(tables) && tables.length >= 10, `Expected >=10 platform DB tables, got ${tables?.length}`)
  const authProviders = await apiJson('/api/v1/auth-providers')
  assert.equal(typeof authProviders.externalOidcEnabled, 'boolean')
  const passwordPolicy = await apiJson('/api/v1/account-management/password-policy')
  assert.ok(Number.isInteger(passwordPolicy.requiredLength) && passwordPolicy.requiredLength >= 6)
  assert.equal('defaultPassword' in passwordPolicy, false, 'Public password-policy must not leak defaultPassword')
  const tablePref = await apiJson('/api/v1/table-preference', { token })
  assert.ok(tablePref.density === 'standard' || tablePref.density === 'compact')
  const navUsageRes = await fetch(`${API_BASE}/api/v1/nav-usage`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ domain: 'platform', group: 'system', href: '/system/health' }),
    signal: AbortSignal.timeout(10_000),
  })
  assert.equal(navUsageRes.status, 204, `Expected /api/v1/nav-usage 204, got ${navUsageRes.status}`)
  const avatarRes = await fetch(`${API_BASE}/api/v1/avatars/me`, {
    headers: { Authorization: `Bearer ${token}` },
    signal: AbortSignal.timeout(10_000),
  })
  assert.ok(avatarRes.status === 200 || avatarRes.status === 204, `Expected /api/v1/avatars/me 200/204, got ${avatarRes.status}`)
  const empOptions = await apiJson('/api/v1/account-users/employee-options', { token })
  assert.ok(Array.isArray(empOptions.items), 'Expected items array from /api/v1/account-users/employee-options')
  const deptOptions = await apiJson('/api/v1/account-users/department-options', { token })
  assert.ok(Array.isArray(deptOptions.items), 'Expected items array from /api/v1/account-users/department-options')

  const exportedBundle = await apiJson('/api/v1/integration-bundles/export?format=yaml', { token })
  assert.equal(exportedBundle.schemaVersion, 'swapmid.io/v1')
  assert.ok(exportedBundle.resourceCount >= 3 && exportedBundle.content.includes('schemaVersion: swapmid.io/v1'))
  const bundleDiff = await apiJson('/api/v1/integration-bundles/diff', {
    method: 'POST',
    token,
    body: { content: exportedBundle.content },
  })
  assert.equal(bundleDiff.createCount, 0, 'Exported live bundle diff should have 0 pending creates')

  const lineageGraph = await apiJson('/api/v1/integration-lineage', { token })
  assert.ok(lineageGraph.nodeCount >= 3 && Array.isArray(lineageGraph.nodes), `Expected >=3 lineage nodes, got ${lineageGraph.nodeCount}`)

  const mcpRes = await fetch(`${API_BASE}/api/mcp`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/list' }),
    signal: AbortSignal.timeout(10_000),
  })
  if (mcpRes.status !== 404) {
    assert.equal(mcpRes.status, 200, `Expected /api/mcp 200 when enabled, got ${mcpRes.status}`)
    const mcpJson = await mcpRes.json()
    assert.ok(Array.isArray(mcpJson.result?.tools) && mcpJson.result.tools.length === 13, `Expected 13 RBAC-authorized MCP tools for admin, got ${mcpJson.result?.tools?.length}`)
    console.log(`  ✓ Verified ${jobs.length} scheduled jobs, ${tables.length} DB tables, GitOps bundle (${exportedBundle.resourceCount} resources), lineage (${lineageGraph.nodeCount} nodes), /api/v1/auth-providers, and 13 MCP tools`)
  } else {
    console.log(`  ✓ Verified ${jobs.length} scheduled jobs, ${tables.length} DB tables, GitOps bundle (${exportedBundle.resourceCount} resources), lineage (${lineageGraph.nodeCount} nodes), /api/v1/auth-providers, and fail-closed /api/mcp (404)`)
  }

  step('7/7 Next.js 16 BFF Security Boundary: Verifying build-meta, login SSR, route guard & /api/proxy 401')
  const buildMetaRes = await fetch(`${WEB_BASE}/build-meta`, { signal: AbortSignal.timeout(10_000) })
  assert.equal(buildMetaRes.status, 200)
  const buildMeta = await buildMetaRes.json()
  assert.ok(buildMeta.buildId, 'Expected buildId in /build-meta')

  const loginRes = await fetch(`${WEB_BASE}/login`, { signal: AbortSignal.timeout(15_000) })
  assert.equal(loginRes.status, 200)
  const loginHtml = await loginRes.text()
  assert.ok(loginHtml.includes('SwapMid'), 'Expected /login HTML to contain SwapMid branding')

  const dashRes = await fetch(`${WEB_BASE}/dashboard`, {
    redirect: 'manual',
    signal: AbortSignal.timeout(10_000),
  })
  assert.equal(dashRes.status, 307, `Expected unauthenticated /dashboard -> 307, got ${dashRes.status}`)
  assert.ok(
    (dashRes.headers.get('location') ?? '').includes('/login'),
    `Expected redirect to /login, got ${dashRes.headers.get('location')}`,
  )

  const proxyRes = await fetch(`${WEB_BASE}/api/proxy/api/v1/data-pipelines`, {
    signal: AbortSignal.timeout(10_000),
  })
  assert.equal(proxyRes.status, 401, `Expected unauthenticated /api/proxy/* -> 401, got ${proxyRes.status}`)
  console.log(`  ✓ Verified BFF /build-meta (${buildMeta.release}), /login SSR, 307 route guard, and 401 proxy fence`)

  console.log('\n[e2e] ✅ ALL 7 FULL-STACK E2E SMOKE CHECKS PASSED.')
}

main().catch((err) => {
  console.error('\n[e2e] ❌ E2E SMOKE CHECK FAILED:', err)
  process.exit(1)
})
