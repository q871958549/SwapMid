import { describe, expect, it } from 'vitest'
import { appendExecutionFilters } from './execution-query'

describe('appendExecutionFilters', () => {
  it('把统计卡的服务器本地时间窗口原样映射到 StartedFrom', () => {
    const params = appendExecutionFilters(new URLSearchParams(), {
      status: '2',
      windowStartedAt: '2026-07-21T08:30:15.1234567',
      windowEndedAt: '2026-07-22T08:30:15.1234567',
    })

    expect(params.get('Status')).toBe('2')
    expect(params.get('StartedFrom')).toBe('2026-07-21T08:30:15.1234567')
    expect(params.get('StartedTo')).toBe('2026-07-22T08:30:15.1234567')
  })

  it('精确统计窗口优先于日期控件，普通日期上限仍覆盖完整自然日', () => {
    const params = appendExecutionFilters(new URLSearchParams(), {
      startedFrom: '2026-07-20',
      windowStartedAt: '2026-07-21T08:30:15',
      windowEndedAt: '2026-07-22T08:30:15',
      startedTo: '2026-07-22',
    })

    expect(params.get('StartedFrom')).toBe('2026-07-21T08:30:15')
    expect(params.get('StartedTo')).toBe('2026-07-22T08:30:15')
  })

  it('普通日期筛选覆盖自然日最后一秒内的全部小数精度', () => {
    const params = appendExecutionFilters(new URLSearchParams(), {
      startedFrom: '2026-07-20',
      startedTo: '2026-07-22',
    })

    expect(params.get('StartedFrom')).toBe('2026-07-20')
    expect(params.get('StartedTo')).toBe('2026-07-22 23:59:59.9999999')
  })
})
