import { describe, expect, it } from 'vitest'
import {
  classifyExecutionHttpFailure,
  classifyExecutionResult,
  readExecutionApiError,
} from './execution-safety'

describe('ETL 稳定 RunId 安全判定', () => {
  it.each([500, 502, 503, 504, 408, 499])('HTTP %s 保留 RunId，禁止生成新运行', (status) => {
    expect(classifyExecutionHttpFailure(status, null, false)).toBe('retain-run-id')
  })

  it('同 RunId 重放遇到 Busy 仍保留，新运行入场前 Busy 才可清除', () => {
    expect(classifyExecutionHttpFailure(409, 'SwapMid:ETL:013', true)).toBe('retain-run-id')
    expect(classifyExecutionHttpFailure(409, 'SwapMid:ETL:013', false)).toBe('clear-run-id')
  })

  it('只有 015 能把当前 RunId 标为 OutcomeUnknown', () => {
    expect(classifyExecutionHttpFailure(409, 'SwapMid:ETL:015', false)).toBe('outcome-unknown')
  })

  it.each([400, 409, 422])('014 即使由 HTTP %s 首次返回也必须保留原 RunId', (status) => {
    expect(classifyExecutionHttpFailure(status, 'SwapMid:ETL:014', false)).toBe('retain-run-id')
  })

  it.each(['SwapMid:ETL:016', 'SwapMid:ETL:019'])('%s 仅在首次请求能证明当前 RunId 未创建', (code) => {
    expect(classifyExecutionHttpFailure(409, code, false)).toBe('target-blocked')
    expect(classifyExecutionHttpFailure(409, code, true)).toBe('retain-run-id')
  })

  it('首次请求的输入/权限失败可清除，重放时只能保留原 RunId', () => {
    expect(classifyExecutionHttpFailure(400, null, false)).toBe('clear-run-id')
    expect(classifyExecutionHttpFailure(403, null, false)).toBe('clear-run-id')
    expect(classifyExecutionHttpFailure(400, null, true)).toBe('retain-run-id')
    expect(classifyExecutionHttpFailure(403, null, true)).toBe('retain-run-id')
    expect(classifyExecutionHttpFailure(409, null, true)).toBe('retain-run-id')
  })

  it('解析 ABP 嵌套错误，并对非 JSON 安全回落', () => {
    expect(readExecutionApiError(JSON.stringify({
      error: { code: 'SwapMid:ETL:016', message: '请先对账' },
    }))).toEqual({ code: 'SwapMid:ETL:016', message: '请先对账' })
    expect(readExecutionApiError('Bad Gateway')).toEqual({ code: null, message: null })
  })

  it('执行状态 5 永远进入对账封锁，未知新值按响应不明处理', () => {
    expect(classifyExecutionResult(5)).toBe('outcome-unknown')
    expect(classifyExecutionResult(2)).toBe('succeeded')
    expect(classifyExecutionResult(3)).toBe('failed')
    expect(classifyExecutionResult(1)).toBe('in-progress')
    expect(classifyExecutionResult(99)).toBe('ambiguous')
  })
})
