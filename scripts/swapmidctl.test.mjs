import assert from 'node:assert/strict'
import test from 'node:test'
import { formatDiffMarkdown, resolveEnvSecretPlaceholders } from './swapmidctl.mjs'

test('resolveEnvSecretPlaceholders substitutes env vars with safe escaping and fails closed when missing', () => {
  const raw = 'config: "{\\"secretKey\\":\\"${ENV_SECRET:MINIO_SECRET}\\",\\"password\\":\\"${ENV_SECRET:PG_PASS}\\"}"\n'
  const { content, resolvedCount } = resolveEnvSecretPlaceholders(raw, {
    MINIO_SECRET: 's3"key\\1',
    PG_PASS: 'pgPass123!',
  })
  assert.equal(resolvedCount, 2)
  assert.ok(content.includes('s3\\"key\\\\1'))
  assert.ok(content.includes('pgPass123!'))

  assert.throws(
    () => resolveEnvSecretPlaceholders(raw, { MINIO_SECRET: 'only-one' }),
    /Missing required environment secret\(s\).*PG_PASS/,
  )
})

test('formatDiffMarkdown emits PR-ready GitHub Markdown table', () => {
  const md = formatDiffMarkdown({
    summary: 'Create 1 / Update 0 / Unchanged 2',
    items: [
      { action: 'Create', kind: 'CdcStream', name: 'orders-proto', changedFields: [], note: 'Draft' },
      { action: 'Update', kind: 'ApiRoute', name: 'health', changedFields: ['upstreamUrl'], note: 'host|changed' },
    ],
  })
  assert.ok(md.includes('### SwapMid GitOps Drift Plan'))
  assert.ok(md.includes('| `Create` | `CdcStream` | `orders-proto` | - | Draft |'))
  assert.ok(md.includes('host\\|changed'))
})
