import assert from 'node:assert/strict'
import test from 'node:test'
import { scanTerminologyText } from './check-terminology.mjs'

test('blocks Chinese middleware mistranslations for SwapMid hub', () => {
  const bad = [
    'SwapMid 是企业数据交换与集成中间件',
    'SwapMid 是一款数据交换中间件',
    '企业级集成与数据交换中间件',
    '交换中间系统',
  ]
  for (const sample of bad) {
    const violations = scanTerminologyText(sample, 'README.zh-CN.md')
    assert.ok(violations.length >= 1, `Expected violation for: ${sample}`)
  }
})

test('blocks Chinglish English mistranslations', () => {
  const bad = [
    'SwapMid is an Enterprise Integration & Data Swap Middleware.',
    'An open-source enterprise data swap control plane.',
    'Enterprise Data Exchange Middleware built on .NET 10.',
    'SwapMid Enterprise Middle Platform.',
    'Enterprise Integration Platform Management Center',
  ]
  for (const sample of bad) {
    const violations = scanTerminologyText(sample, 'README.md')
    assert.ok(violations.length >= 1, `Expected violation for: ${sample}`)
  }
})

test('allows canonical positioning and legitimate technical middleware references', () => {
  const good = [
    'SwapMid 是一款生产级开源企业级数据交换与集成中台，编排 Kafka、RabbitMQ 与 Redis 等底层基础设施中间件。',
    'SwapMid is an open-source, production-grade Enterprise Data Exchange & Integration Platform.',
    'Next.js Proxy Middleware & /api/proxy/* BFF boundary.',
    'ASP.NET Core auditing middleware records every HTTP API call.',
  ]
  for (const sample of good) {
    const violations = scanTerminologyText(sample, 'docs/architecture.md')
    assert.deepEqual(violations, [], `Unexpected violation for: ${sample}`)
  }
})
