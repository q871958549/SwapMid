#!/usr/bin/env node
/**
 * swapmidctl — Zero-dependency GitOps CLI for SwapMid (swapmid.io/v1)
 *
 * Commands:
 *   export [--format yaml|json] [-o bundle.yaml]
 *   diff   -f <bundle.yaml|json>
 *   apply  -f <bundle.yaml|json> [--dry-run=true|false] [--update-existing]
 *   health
 *
 * Environment variables:
 *   SWAPMID_API_URL   (default: http://127.0.0.1:18080)
 *   SWAPMID_TOKEN     (optional Bearer JWT; if unset, signs in via password grant)
 *   SWAPMID_USER      (default: admin)
 *   SWAPMID_PASSWORD  (default: 1q2w3E*)
 */
import { readFileSync, writeFileSync } from 'node:fs'

const API_BASE = (process.env.SWAPMID_API_URL ?? process.env.API_BASE_URL ?? 'http://127.0.0.1:18080').replace(/\/+$/, '')

function parseArgs(argv) {
  const [cmd, ...rest] = argv
  const opts = { format: 'yaml', output: null, file: null, dryRun: true, updateExisting: false }
  for (let i = 0; i < rest.length; i += 1) {
    const arg = rest[i]
    if ((arg === '--format' || arg === '-t') && rest[i + 1]) {
      opts.format = rest[++i]
    } else if ((arg === '--output' || arg === '-o') && rest[i + 1]) {
      opts.output = rest[++i]
    } else if ((arg === '--file' || arg === '-f') && rest[i + 1]) {
      opts.file = rest[++i]
    } else if (arg === '--no-dry-run' || arg === '--dry-run=false' || arg === '--execute') {
      opts.dryRun = false
    } else if (arg === '--dry-run' || arg === '--dry-run=true') {
      opts.dryRun = true
    } else if (arg === '--update-existing') {
      opts.updateExisting = true
    }
  }
  return { cmd, opts }
}

async function resolveToken() {
  if (process.env.SWAPMID_TOKEN) return process.env.SWAPMID_TOKEN
  const res = await fetch(`${API_BASE}/connect/token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({
      grant_type: 'password',
      username: process.env.SWAPMID_USER ?? 'admin',
      password: process.env.SWAPMID_PASSWORD ?? '1q2w3E*',
      client_id: 'SwapMid_App',
      scope: 'openid profile email roles SwapMid',
    }),
  })
  if (!res.ok) {
    throw new Error(`Authentication failed (HTTP ${res.status}): ${await res.text()}`)
  }
  const data = await res.json()
  return data.access_token
}

async function apiJson(path, { method = 'GET', token, body } = {}) {
  const headers = { Accept: 'application/json' }
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const res = await fetch(`${API_BASE}${path}`, {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  const text = await res.text()
  if (!res.ok) {
    throw new Error(`${method} ${path} failed (HTTP ${res.status}): ${text.slice(0, 500)}`)
  }
  return text ? JSON.parse(text) : null
}

function printHelp() {
  console.log(`swapmidctl — SwapMid GitOps CLI (swapmid.io/v1)

Usage:
  node scripts/swapmidctl.mjs health
  node scripts/swapmidctl.mjs export [--format yaml|json] [-o bundle.yaml]
  node scripts/swapmidctl.mjs diff -f <bundle.yaml|json>
  node scripts/swapmidctl.mjs apply -f <bundle.yaml|json> [--dry-run | --execute] [--update-existing]
`)
}

async function main() {
  const { cmd, opts } = parseArgs(process.argv.slice(2))
  if (!cmd || cmd === '--help' || cmd === '-h') {
    printHelp()
    return
  }

  if (cmd === 'health') {
    const status = await apiJson('/health-status')
    console.log(JSON.stringify(status, null, 2))
    return
  }

  const token = await resolveToken()

  if (cmd === 'export') {
    const format = opts.format === 'json' ? 'json' : 'yaml'
    const result = await apiJson(`/api/v1/integration-bundles/export?format=${format}`, { token })
    if (opts.output) {
      writeFileSync(opts.output, result.content, 'utf8')
      console.error(`[swapmidctl] Exported ${result.resourceCount} resources (${result.schemaVersion}) -> ${opts.output}`)
    } else {
      process.stdout.write(result.content)
    }
    return
  }

  if (cmd === 'diff' || cmd === 'apply') {
    if (!opts.file) {
      throw new Error(`Missing required -f <bundle.yaml|json> argument for '${cmd}'.`)
    }
    const content = readFileSync(opts.file, 'utf8')
    if (cmd === 'diff') {
      const diff = await apiJson('/api/v1/integration-bundles/diff', {
        method: 'POST',
        token,
        body: { content },
      })
      console.log(diff.summary)
      for (const item of diff.items) {
        const fields = item.changedFields?.length ? ` (${item.changedFields.join(', ')})` : ''
        const note = item.note ? ` — ${item.note}` : ''
        console.log(`  [${item.action}] ${item.kind}/${item.name}${fields}${note}`)
      }
      return
    }

    const applied = await apiJson('/api/v1/integration-bundles/apply', {
      method: 'POST',
      token,
      body: { content, dryRun: opts.dryRun, allowUpdateExisting: opts.updateExisting },
    })
    console.log(applied.summary)
    for (const item of applied.items) {
      const note = item.note ? ` — ${item.note}` : ''
      console.log(`  [${item.action}] ${item.kind}/${item.name}${note}`)
    }
    if (applied.failedCount > 0) process.exitCode = 1
    return
  }

  printHelp()
  process.exitCode = 1
}

main().catch((err) => {
  console.error(`[swapmidctl] ERROR: ${err.message}`)
  process.exit(1)
})
