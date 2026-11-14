import assert from 'node:assert/strict'
import test from 'node:test'

import { collectMissingSummaries, compareWithBaseline } from './check-api-summary.mjs'

function spec(paths) {
  return { paths }
}

test('只统计 /api/v1 业务接口，忽略兼容层与非 HTTP 方法键', () => {
  const result = collectMissingSummaries(spec({
    '/api/v1/samples': { get: { tags: ['Sample'] }, parameters: [{ name: 'ignored' }] },
    '/api/jeecg/legacy': { get: { tags: ['Legacy'] } },
    '/connect/token': { post: { tags: ['Auth'] } },
  }))

  assert.equal(result.total, 1)
  assert.equal(result.missing, 1)
  assert.deepEqual(result.byTag, { Sample: 1 })
})

test('空白 summary 与缺失 summary 同等对待', () => {
  const result = collectMissingSummaries(spec({
    '/api/v1/a': { get: { tags: ['T'], summary: '查询清单' } },
    '/api/v1/b': { get: { tags: ['T'], summary: '   ' } },
    '/api/v1/c': { get: { tags: ['T'] } },
  }))

  assert.equal(result.total, 3)
  assert.equal(result.missing, 2)
  assert.deepEqual(result.byTag, { T: 2 })
})

test('无 tag 的接口归入 Uncategorized，不会被漏掉', () => {
  const result = collectMissingSummaries(spec({ '/api/v1/x': { post: {} } }))

  assert.deepEqual(result.byTag, { Uncategorized: 1 })
})

test('已有 controller 缺失数升高即回归', () => {
  const current = { total: 10, missing: 3, byTag: { Sample: 3 } }
  const { regressions, newTags } = compareWithBaseline(current, { byTag: { Sample: 2 } })

  assert.deepEqual(newTags, [])
  assert.deepEqual(regressions, [{ tag: 'Sample', count: 3, baseline: 2 }])
})

test('基线里没有的新 controller 必须零缺失', () => {
  const { newTags } = compareWithBaseline(
    { total: 2, missing: 1, byTag: { Fresh: 1 } },
    { byTag: { Sample: 5 } },
  )

  assert.deepEqual(newTags, [{ tag: 'Fresh', count: 1 }])
})

test('新 controller 全部写了 summary 时不进 newTags（不该拦住合规的新代码）', () => {
  // 全部写了注释 → 不出现在 byTag 里，比对自然放行
  const current = collectMissingSummaries(spec({
    '/api/v1/fresh': { get: { tags: ['Fresh'], summary: '查询清单' } },
  }))
  const { regressions, newTags } = compareWithBaseline(current, { byTag: { Sample: 5 } })

  assert.deepEqual(current.byTag, {})
  assert.deepEqual(regressions, [])
  assert.deepEqual(newTags, [])
})

test('缺失数下降时报告可收紧的 controller', () => {
  const { regressions, improvements } = compareWithBaseline(
    { total: 10, missing: 1, byTag: { Sample: 1 } },
    { byTag: { Sample: 4, Vehicle: 2 } },
  )

  assert.deepEqual(regressions, [])
  assert.deepEqual(improvements, [
    { tag: 'Sample', count: 1, baseline: 4 },
    { tag: 'Vehicle', count: 0, baseline: 2 },
  ])
})
