import { describe, expect, it } from 'vitest'
import {
  isDurationAnomaly,
  successfulDurationMedian,
  type JobExecution,
} from './execution-shared'

function execution(overrides: Partial<JobExecution>): JobExecution {
  return {
    id: 'execution-id',
    jobKey: 'job-key',
    jobType: 'ExampleJob',
    jobMethod: null,
    triggerKind: 0,
    status: 1,
    startedAt: '2026-07-22T08:00:00',
    finishedAt: '2026-07-22T08:00:01',
    durationMs: 1000,
    summary: '完成',
    error: null,
    isDryRun: false,
    argsJson: null,
    creatorId: null,
    ...overrides,
  }
}

describe('执行耗时窗口基线', () => {
  it('只统计成功且有有效耗时的记录，并正确计算奇偶样本中位数', () => {
    expect(successfulDurationMedian([
      execution({ durationMs: 300 }),
      execution({ durationMs: 100 }),
      execution({ durationMs: 200 }),
      execution({ status: 2, durationMs: 10_000 }),
      execution({ status: 4, durationMs: 20_000 }),
      execution({ durationMs: null }),
    ])).toBe(200)

    expect(successfulDurationMedian([
      execution({ durationMs: 100 }),
      execution({ durationMs: 200 }),
      execution({ durationMs: 300 }),
      execution({ durationMs: 400 }),
    ])).toBe(250)
  })

  it('少于三个成功样本时不建立耗时异常基线', () => {
    expect(successfulDurationMedian([
      execution({ durationMs: 100 }),
      execution({ durationMs: 300 }),
      execution({ status: 2, durationMs: 10_000 }),
    ])).toBeNull()
  })

  it('正式执行与演练分别建立基线，避免不同工作量互相污染', () => {
    const rows = [
      execution({ durationMs: 100, isDryRun: false }),
      execution({ durationMs: 200, isDryRun: false }),
      execution({ durationMs: 300, isDryRun: false }),
      execution({ durationMs: 10, isDryRun: true }),
      execution({ durationMs: 20, isDryRun: true }),
      execution({ durationMs: 30, isDryRun: true }),
    ]

    expect(successfulDurationMedian(rows, false)).toBe(200)
    expect(successfulDurationMedian(rows, true)).toBe(20)
  })

  it('仅在耗时严格超过成功中位数两倍时标记异常，无基线时不误报', () => {
    expect(isDurationAnomaly(401, 200)).toBe(true)
    expect(isDurationAnomaly(400, 200)).toBe(false)
    expect(isDurationAnomaly(1000, null)).toBe(false)
    expect(isDurationAnomaly(1000, 0)).toBe(false)
  })
})
